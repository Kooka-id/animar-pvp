using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections.Generic;

public class ActionCardManagerbackup : NetworkBehaviour
{
    [Header("Action Card Pool")]
    public List<ActionCardData> actionCards;

    [Header("UI Placeholders (CardView)")]
    // Ganti List<Button> menjadi CardView
    public List<CardView> cardVisuals;

    private List<ActionCardData> drawnCards = new List<ActionCardData>();
    private bool[] availableCards;

    private int playersPickedCount = 0;

    // SERVER: mulai ronde
    [ServerRpc(RequireOwnership = false)]
    public void StartRoundServerRpc()
    {
        if (!IsServer) return;
        // Wajib reset PlayerData di sini sebelum Draw
        // ResetPlayersForNewRound(); 
        ResetOverlayClientRpc();
        DrawCardsOnServer();
    }

    [ClientRpc]
    private void ResetOverlayClientRpc()
    {
        CardView.ResetOverlayVisuals();
    }

    private void DrawCardsOnServer()
    {
        List<int> selectedIndexes = new List<int>();
        List<ActionCardData> tempPool = new List<ActionCardData>(actionCards);

        // Logika Random 3 Kartu
        for (int i = 0; i < cardVisuals.Count; i++)
        {
            int randIndex = Random.Range(0, tempPool.Count);
            selectedIndexes.Add(actionCards.IndexOf(tempPool[randIndex]));
            tempPool.RemoveAt(randIndex);
        }

        availableCards = new bool[selectedIndexes.Count];
        for (int i = 0; i < availableCards.Length; i++)
            availableCards[i] = true;

        SendDrawClientRpc(selectedIndexes.ToArray());
    }

    // CLIENT: terima draw card dari server
    [ClientRpc]
    private void SendDrawClientRpc(int[] indexes)
    {
        drawnCards.Clear();
        availableCards = new bool[indexes.Length];
        for (int i = 0; i < availableCards.Length; i++)
            availableCards[i] = true;
    
        foreach (var cardV in cardVisuals)
        {
            if (!cardV.gameObject.activeSelf)
            {
                cardV.gameObject.SetActive(true);
            }
        }

        for (int i = 0; i < indexes.Length; i++)
        {
            ActionCardData selected = actionCards[indexes[i]];
            drawnCards.Add(selected);

            CardView cardV = cardVisuals[i];
            cardV.SetupCard(selected); // Reset visual dan set sprite depan

            int index = i;
            Button btn = cardV.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardChosen(index));
            btn.interactable = true;
        }
    }

    // Player klik kartu
    private void OnCardChosen(int index)
    {
        // PENTING: JANGAN PANGGIL cardV.FlipCard() di sini!
        // Hanya kirim input ke Server

        Debug.Log($"[LOCAL] Try pick card {drawnCards[index].cardName}");
        SendChoiceServerRpc(index);
    }

    // SERVER: terima pilihan player
    [ServerRpc(RequireOwnership = false)]
    private void SendChoiceServerRpc(int cardIndex, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        var playerObj = NetworkManager.Singleton.ConnectedClients[senderId].PlayerObject;
        var player = playerObj.GetComponent<PlayerData>();

        if (availableCards[cardIndex] && !player.HasPickedCard.Value)
        {
            availableCards[cardIndex] = false;
            player.HasPickedCard.Value = true;

            ActionCardData chosen = drawnCards[cardIndex];
            Debug.Log($"[SERVER] Player {senderId} chose {chosen.cardName}");

            LockCardClientRpc(cardIndex, senderId);
            ApplyEffect(chosen, senderId);

            playersPickedCount++;

            if (playersPickedCount >= NetworkManager.Singleton.ConnectedClients.Count)
            {
                // Semua pemain sudah memilih, kirim sinyal REVEAL
                RevealBattleCardsClientRpc();
            }
        }
        else
        {
            Debug.Log($"[SERVER] Player {senderId} tried to pick unavailable card!");
        }
    }

    // CLIENT: lock kartu setelah dipilih
    [ClientRpc]
    // Tambahkan rpcParams di sini biar tahu siapa yang ngirim dari Server
    private void LockCardClientRpc(int cardIndex, ulong pickerId)
    {
        CardView cardV = cardVisuals[cardIndex];
        Button btn = cardV.GetComponent<Button>();

        // 1. Lock Interaksi
        //btn.interactable = false;
        // cardV.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f); <-- Hati-hati dengan ini, bisa bentrok sama UI/Visual

        // 2. FLIP HANYA JIKA ITU KARTU PILIHAN LU
        if (pickerId == NetworkManager.Singleton.LocalClientId) // <--- Kartu Pilihan Lokal Kita
        {
            // PENTING: Hanya panggil Flip() biasa, BUKAN FlipAndZoom()
            cardV.Flip(true, 0.3f); 
            
            // Ganti Listener: Hapus listener lama (OnCardChosen)
            btn.onClick.RemoveAllListeners();
            // Tambah listener baru: OnCardZoom
            btn.onClick.AddListener(() => OnCardZoom(cardIndex));

            // Karena sudah ke-flip, kita set visual lock/interactable ke true
            // Ini memastikan tombol tetap aktif untuk klik kedua (Zoom)
            // SetLockedVisual(false) akan menghapus teks 'LOCKED' kalau sebelumnya ada
            cardV.SetLockedVisual(false); 
            
            Debug.Log($"[CLIENT] Your chosen card ({cardIndex}) is flipped. Ready for Zoom.");
        }
        else // Kartu Pilihan Lawan
        {
            // Nonaktifkan interaksi tombol
            btn.interactable = false;
            
            // Tampilkan visual locked (menggelapkan kartu)
            cardV.SetLockedVisual(true);
            
            // Pastikan dia tetap tertutup
            if(cardV.IsFlipped) cardV.ShowBackInstant(); 
            
            Debug.Log($"[CLIENT] Card ({cardIndex}) locked by opponent.");
        }
        
        // 3. Matikan interaksi untuk kartu-kartu yang TIDAK dipilih
        // Kita loop lagi di semua kartu yang belum di-lock
        for (int i = 0; i < cardVisuals.Count; i++)
        {
            // Logika ini butuh sinkronisasi data global, sementara kita fokus di kartu yang *available*
            // di Manager (yang udah di-lock Server)
            if (availableCards[i] == false) // Ini kartu yang sudah dipilih (Lock/Flip)
            {
                // Sudah di-handle di atas
            }
            else // Kartu yang belum dipilih
            {
                // Matikan interaksi kartu yang tersisa.
                cardVisuals[i].GetComponent<Button>().interactable = false;
            }
        }
    }

    [ClientRpc]
    private void RevealBattleCardsClientRpc()
    {
        // === 1. Tambahkan Jeda 5 Detik Dulu ===
        LeanTween.delayedCall(gameObject, 5f, () =>
        {
            // --- Logika Reveal di sini ---
            
            CardView localChosenCard = null;
            CardView opponentChosenCard = null;
            
            // 1. Identifikasi Kartu yang Dipilih
            for (int i = 0; i < cardVisuals.Count; i++)
            {
                CardView cardV = cardVisuals[i];
                
                // A. Kartu Lokal (Di-Flip dan sudah dipilih Server)
                // Cek IsFlipped aja karena sudah di-LockCardClientRpc
                if (cardV.IsFlipped) 
                {
                    // Kalau IsFlipped true, itu pasti kartu lokal yang dipilih
                    localChosenCard = cardV;
                    continue;
                }

                // B. Kartu Lawan (Di-Lock dan Belum Flip)
                // availableCards[i] == false artinya kartu ini sudah dikunci oleh Server
                if (availableCards[i] == false && !cardV.IsFlipped) 
                {
                    opponentChosenCard = cardV;
                }
            }
            
            // 2. Eksekusi Reset Zoom dan Reveal Lawan
            
            // KARTU LOKAL: Cek state Zoom-nya, lalu pindahkan/reset
            if (localChosenCard != null)
            {
                HandleLocalCardReveal(localChosenCard); // Memanggil fungsi pengecek state
            }

            // KARTU LAWAN: Flip, Hilangkan Lock Visual, dan Pindah ke Kanan
            if (opponentChosenCard != null)
            {
                opponentChosenCard.SetLockedVisual(false); // Hilangkan visual lock
                opponentChosenCard.FlipAndMoveForReveal(false); // Flip, Move, dan Scale ke posisi Reveal
            }
            
            // 3. Sembunyikan Kartu yang TIDAK Dipilih
            foreach (var cardV in cardVisuals)
            {
                if (cardV != localChosenCard && cardV != opponentChosenCard)
                {
                    cardV.HideCardVisuals(); 
                }
            }
            
            Debug.Log("[CLIENT] Reveal Final Kartu Selesai! Dua kartu Battle sudah diposisikan.");

        }).setIgnoreTimeScale(false); // setIgnoreTimeScale(false) agar jeda 5 detik tidak terpengaruh Time.timeScale
    }

    private void HandleLocalCardReveal(CardView localChosenCard)
    {
        // Perbandingan skala untuk menentukan apakah sedang di-zoom.
        // Skala (1, 1, 1) adalah skala Zoom maksimal.
        bool currentlyZoomed = localChosenCard.transform.localScale != localChosenCard.OriginalScale;

        if (currentlyZoomed)
        {
            // KONDISI 1: Kartu MASIH di-Zoom ketika waktu habis.
            // Panggil fungsi yang lu mau: Reset Zoom + Pindah ke posisi reveal.
            localChosenCard.ResetZoomForReveal(true, 0.5f); 
            Debug.Log("[CLIENT] Kartu Lokal: Masih di-ZOOM, memanggil ResetZoomForReveal.");
        }
        else
        {
            // KONDISI 2: Kartu TIDAK di-Zoom (berada di posisi awal/kecil).
            // Kita hanya perlu menggeser/memindahkan kartu ke posisi reveal.
            
            Vector3 targetPosition = CardView.TARGET_POS_PLAYER; // Target Kiri Tengah
            float duration = 0.5f;

            // 1. Pindah posisi ke target (kartu sudah terbuka/flipped)
            LeanTween.moveLocal(localChosenCard.gameObject, targetPosition, duration)
                .setEase(LeanTweenType.easeOutQuad);

            // 2. Kembalikan ke skala original (kecil)
            // Kartu sudah di skala original, tapi kita panggil lagi untuk jaga-jaga
            LeanTween.scale(localChosenCard.gameObject, localChosenCard.OriginalScale, duration)
                .setEase(LeanTweenType.easeOutQuad);
            
            // 3. Matikan Overlay (Jika aktif)
            // Kartu di posisi awal harusnya Overlay sudah mati, tapi kita matikan lagi
            // (Asumsi di OnCardZoom, overlay cuma mati di ResetZoom. Kalau di posisi awal, overlay bisa aja masih on)
            // KITA BISA MENGANDALKAN ResetZoomForReveal kartu lokal untuk mematikan overlay, 
            // tapi jika kartu kita tidak di-zoom, overlay tidak aktif. Jadi tidak perlu matikan overlay di sini.
            // **CATATAN**: Di kode lu, hanya `ResetZoomForReveal` yang mematikan Overlay.
            // Kalau kartu kita tidak di-zoom, Overlay harusnya sudah mati (jika kita matikan di ResetZoom)
            // KITA MATIKAN OVERLAY DI FUNGSI BARU INI KARENA KARTU LOKAL BERTANGGUNG JAWAB.
            
            // Cek Overlay dan matikan jika perlu (durasi 0.3s)
            GameObject overlayObj = GameObject.Find("OverlayPanel");
            if (overlayObj != null)
            {
                CanvasGroup overlayCG = overlayObj.GetComponent<CanvasGroup>();
                if (overlayCG != null && overlayCG.alpha > 0f)
                {
                    LeanTween.alphaCanvas(overlayCG, 0f, 0.3f);
                    overlayCG.interactable = false;
                    overlayCG.blocksRaycasts = false;
                    overlayCG.transform.SetAsFirstSibling();
                }
            }
            
            Debug.Log("[CLIENT] Kartu Lokal: Tidak di-ZOOM, hanya memindahkan ke posisi reveal.");
        }
    }

    // Terapkan efek kartu
    private void ApplyEffect(ActionCardData card, ulong playerId)
    {
        switch (card.effectType)
        {
            case EffectType.BuffAttack:
                Debug.Log($"Player {playerId} Attack UP {card.value * 100}%");
                break;
            case EffectType.BuffDefense:
                Debug.Log($"Player {playerId} Defense UP {card.value * 100}%");
                break;
            case EffectType.DebuffAttack:
                Debug.Log($"Enemy of {playerId} Attack DOWN {card.value * 100}%");
                break;
            case EffectType.DebuffDefense:
                Debug.Log($"Enemy of {playerId} Defense DOWN {card.value * 100}%");
                break;
            case EffectType.Heal:
                Debug.Log($"Player {playerId} Heal {card.value * 100}% HP");
                break;
        }
    }
    
    private void OnCardZoom(int index)
    {
        CardView cardV = cardVisuals[index];
        
        // Perbandingan skala untuk menentukan apakah sedang di-zoom atau tidak.
        // Skala (1, 1, 1) biasanya adalah skala Zoom maksimal (terpusat).
        // Jika skala saat ini BUKAN skala aslinya, berarti dia sedang di-zoom/diperbesar.
        bool currentlyZoomed = cardV.transform.localScale != cardV.OriginalScale;

        if (currentlyZoomed)
        {
            // Kondisi 2.1: Kartu sedang di-Zoom -> Lakukan RESET ZOOM
            
            // Panggil ResetZoom (balik ke skala dan posisi original, dan matikan overlay)
            cardV.ResetZoom(0.3f);
            
            // Setelah di-reset, tombol HARUS kembali interaktif untuk klik Zoom lagi
            cardV.GetComponent<Button>().interactable = true;
            
            Debug.Log($"[LOCAL] Kartu {drawnCards[index].cardName} di-RESET ke posisi awal.");
        }
        else
        {
            // Kondisi 2.2: Kartu sudah di-Flip tapi di posisi awal -> Lakukan ZOOM
            
            // Panggil fungsi ZoomCardToCenter yang sudah kita buat
            cardV.ZoomCardToCenter(0.5f);
            
            // Tombol akan dimatikan di dalam ZoomCardToCenter saat transisi
            
            Debug.Log($"[LOCAL] Kartu {drawnCards[index].cardName} di-ZOOM ke tengah.");
        }
    }
}
