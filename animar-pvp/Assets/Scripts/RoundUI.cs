using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class RoundUI : NetworkBehaviour
{
    [Header("References")]
    public ActionCardManager cardManager;   // script yang handle draw kartu
    public Button startRoundButton;         // tombol mulai ronde
    
    public TMP_Text roundInfoText;          // optional: tampilkan info ronde

    private int roundNumber = 0;

    public override void OnNetworkSpawn()
    {
        // Setup listener tombol sekali saja
        startRoundButton.onClick.AddListener(OnStartRoundClicked);

        // Kalau bukan server → disable tombol
        if (!IsServer)
        {
            startRoundButton.interactable = false;
        }
        else
        {
            startRoundButton.interactable = true; // host aktif
        }
    }

    private void OnStartRoundClicked()
    {
        if (IsServer)
        {
            roundNumber++;
            roundInfoText.text = $"Round {roundNumber} started!";

            // Reset semua pemain
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject.GetComponent<PlayerData>();
                player.ResetForNewRound();
            }

            // kasih tau semua client untuk draw kartu baru
            cardManager.StartRoundServerRpc();
        }
    }
}
