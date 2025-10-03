using UnityEngine;
using UnityEngine.UI;
using System.Collections; // Wajib jika menggunakan LeanTween

public class CardViewbackup : MonoBehaviour
{
    [Header("Hierarchy References")]
    // Ini adalah objek yang dirotasi (CardHolder dari Hierarchy)
    [SerializeField] private RectTransform cardHolder;

    // Gambar yang akan aktif saat kartu tertutup
    [SerializeField] private Image backImage;
    // Gambar yang akan aktif saat kartu terbuka
    [SerializeField] private Image frontImage;
    [SerializeField] private GameObject lockText;

    private bool isFlipped = false;
    private Button cardButton;

    private Vector3 originalScale;
    private Vector3 originalPosition;
    private CanvasGroup overlayCanvasGroup;

    [HideInInspector]
    public Vector3 OriginalScale => originalScale;
    public bool IsFlipped => isFlipped;

    // BARU: Posisi Final Reveal (Sesuaikan nilai ini dengan Canvas lu)
    private static readonly Vector3 TARGET_POS_PLAYER = new Vector3(-130f, -600f, 0f); // Kiri Tengah
    private static readonly Vector3 TARGET_POS_OPPONENT = new Vector3(130f, -600f, 0f); // Kanan Tengah

    private void Awake()
    {
        // Pastikan LeanTween sudah diinisiasi di scene
        LeanTween.init();

        // Simpan referensi Button dari Parent
        cardButton = GetComponent<Button>();
        originalScale = transform.localScale;
        originalPosition = transform.localPosition;

        // Pastikan state awal tertutup
        ShowBackInstant();
    }

    private void Start()
    {
        // Cari Overlay Panel berdasarkan nama GameObject di Hierarchy
        GameObject overlayObj = GameObject.Find("OverlayPanel");

        if (overlayObj != null)
        {
            // Ambil komponen CanvasGroup dari objek yang ditemukan
            overlayCanvasGroup = overlayObj.GetComponent<CanvasGroup>();

            if (overlayCanvasGroup == null)
            {
                Debug.LogError("OverlayPanel ditemukan, tapi tidak memiliki komponen CanvasGroup!");
            }
        }
        else
        {
            Debug.LogError("GameObject 'OverlayPanel' tidak ditemukan di Scene!");
        }
    }

    // Dipanggil dari ActionCardManager saat kartu di-random
    public void SetupCard(ActionCardData data)
    {
        frontImage.sprite = data.cardSprite;
        ShowBackInstant(); // Pastikan selalu tertutup saat draw
        cardButton.interactable = true;
    }

    // Mengubah visual kartu ke Belakang (Instan, tanpa animasi)
    public void ShowBackInstant()
    {
        // RESET ROTASI DAN VISUAL (seperti sebelumnya)
        backImage.enabled = true;
        frontImage.enabled = false;
        cardHolder.localRotation = Quaternion.Euler(0, 0, 0);
        isFlipped = false;

        // --- TAMBAHAN PENTING (RESET SKALA & POSISI) ---
        // Pastikan parent (CardView) kembali ke ukuran dan posisi awal
        transform.localScale = originalScale;
        transform.localPosition = originalPosition;
        GetComponent<Image>().color = Color.white;

        SetLockedVisual(false);
        LeanTween.cancel(gameObject);
    }

    public void SetLockedVisual(bool locked)
    {
        if (lockText != null)
        {
            lockText.SetActive(locked); // Tampilkan/Sembunyikan teks LOCKED
        }

        // Gelapkan/Cerahkankan warna kartu
        Image cardImage = GetComponent<Image>();
        if (cardImage != null)
        {
            Color lockedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            cardImage.color = locked ? lockedColor : Color.white;
        }
    }

    // Mengubah visual kartu ke Depan (Instan, tanpa animasi)
    public void ShowFrontInstant()
    {
        backImage.enabled = false;
        frontImage.enabled = true;
        cardHolder.localRotation = Quaternion.Euler(0, 180, 0); // Rotasi 180 (agar front terlihat)
        isFlipped = true;
    }

    // Fungsi Flip Utama (dengan LeanTween)
    public void Flip(bool showFront, float duration = 0.3f)
    {
        if (isFlipped == showFront) return; // Udah di posisi yang diminta

        float targetY = showFront ? 180f : 0f;

        // Simpan state baru yang dituju
        isFlipped = showFront;

        // Langkah 1: Rotasi 90 derajat (menciutkan visual)
        LeanTween.rotateY(cardHolder.gameObject, cardHolder.localRotation.eulerAngles.y + 90f, duration / 2f)
            .setEase(LeanTweenType.easeInSine)
            .setOnComplete(() =>
            {
                // --- Langkah 2: Ganti Visual di Tengah (Sembunyikan/Tampilkan Image) ---
                if (showFront)
                {
                    backImage.enabled = false;
                    frontImage.enabled = true;
                }
                else
                {
                    backImage.enabled = true;
                    frontImage.enabled = false;
                }

                // --- Langkah 3: Rotasi 90 derajat lagi (memunculkan visual baru)
                LeanTween.rotateY(cardHolder.gameObject, targetY, duration / 2f)
                    .setEase(LeanTweenType.easeOutSine);
            });
    }

    public void FlipAndZoom(float zoomDuration = 0.5f)
    {
        // Panggil Flip dulu. Rotasi 3D ini butuh waktu, misal 0.3s
        Flip(true, 0.3f);

        // Kita tunda eksekusi Zoom sampai Flip hampir selesai
        // Pake Delay (misal 0.1s setelah flip mulai)
        LeanTween.delayedCall(gameObject, 0.1f, () =>
        {
            // LOGIKA ZOOM
            Vector2 targetPosition = Vector2.zero;
            Vector3 targetScale = Vector3.one;

            cardButton.interactable = false;

            // Tweening 1: Pindahkan posisi ke (0, 0)
            LeanTween.moveLocal(gameObject, targetPosition, zoomDuration)
                .setEase(LeanTweenType.easeInOutQuad);

            // Tweening 2: Skala menjadi (1, 1, 1)
            LeanTween.scale(gameObject, targetScale, zoomDuration)
                .setEase(LeanTweenType.easeInOutQuad)
                .setOnComplete(() =>
                {
                    Debug.Log("Kartu sudah ter-zoom dan terpusat, siap tunggu 5 detik...");
                    transform.SetAsLastSibling();

                    // Aktifkan Overlay
                    if (overlayCanvasGroup != null)
                    {
                        // Fade in Overlay secara lembut
                        LeanTween.alphaCanvas(overlayCanvasGroup, 1f, 0.3f);
                        overlayCanvasGroup.interactable = true;
                        overlayCanvasGroup.blocksRaycasts = true;
                    }

                    Debug.Log("Kartu Anda di-zoom, menunggu lawan memilih.");
                });
        });
    }

    public void FlipAndMoveForReveal(bool isLocalPlayerCard, float duration = 0.5f)
    {
        Flip(true, 0.3f); // Flip kartu lawan

        Vector3 targetPosition = isLocalPlayerCard ? TARGET_POS_PLAYER : TARGET_POS_OPPONENT;

        LeanTween.delayedCall(gameObject, 0.2f, () =>
        {
            LeanTween.moveLocal(gameObject, targetPosition, duration)
                .setEase(LeanTweenType.easeOutQuad);

            LeanTween.scale(gameObject, originalScale, duration)
                .setEase(LeanTweenType.easeOutQuad);

            // Pindahkan kartu lawan ke lapisan depan
            transform.SetAsLastSibling();
        });
    }

    // Dipanggil oleh Kartu Lokal saat Final Reveal (Reset Scale dan Pindah)
    public void ResetZoomForReveal(bool isLocalPlayerCard, float duration = 0.5f)
    {
        Vector3 targetPosition = isLocalPlayerCard ? TARGET_POS_PLAYER : TARGET_POS_OPPONENT;

        // 1. Pindah posisi ke target
        LeanTween.moveLocal(gameObject, targetPosition, duration)
            .setEase(LeanTweenType.easeOutQuad);

        // 2. Kembalikan ke skala original (kecil)
        LeanTween.scale(gameObject, originalScale, duration)
            .setEase(LeanTweenType.easeOutQuad);

        // 3. Matikan Overlay (KARTU LOKAL BERTANGGUNG JAWAB)
        if (overlayCanvasGroup != null)
        {
            LeanTween.alphaCanvas(overlayCanvasGroup, 0f, 0.3f).setOnComplete(() =>
            {
                overlayCanvasGroup.interactable = false;
                overlayCanvasGroup.blocksRaycasts = false;
                overlayCanvasGroup.transform.SetAsFirstSibling();
            });
        }
    }

    // Fungsi untuk mengembalikan kartu ke posisi dan skala awal (tetap terbuka)
    public void ResetZoom(float duration = 0.3f)
    {
        // Menggunakan variabel yang sudah disimpan di Awake()
        // Kartu TIDAK PERLU di-flip balik, karena dia sudah di-set 'isFlipped = true'
        LeanTween.moveLocal(gameObject, originalPosition, duration);
        LeanTween.scale(gameObject, originalScale, duration)
            .setOnComplete(() =>
            {
                if (overlayCanvasGroup != null)
                {
                    // Fade out Overlay
                    LeanTween.alphaCanvas(overlayCanvasGroup, 0f, 0.3f)
                        .setOnComplete(() =>
                        {
                            // MATIKAN BLOKIR DAN INTERAKSI (Wajib setelah Alpha 0)
                            overlayCanvasGroup.interactable = false;
                            overlayCanvasGroup.blocksRaycasts = false;
                            overlayCanvasGroup.transform.SetAsLastSibling();
                        });
                }
                // Interaksi jangan dikembalikan, karena kartu ini sudah dipilih/locked
                // cardButton.interactable = true; // JANGAN DI-UNCOMMENT
                Debug.Log("Kartu kembali ke posisi awal, tetap terbuka.");
            });
    }
    
    public void HideCardVisuals(float duration = 0.3f)
    {
        // Fade out seluruh parent CardView
        LeanTween.alpha(gameObject, 0f, duration)
            // HAPUS .setDestroyOnComplete(true);
            .setOnComplete(() =>
            {
                // Cukup sembunyikan object-nya, jangan hancurkan!
                gameObject.SetActive(false); 
            });  
    }
}