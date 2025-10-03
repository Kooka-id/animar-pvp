using Unity.Netcode;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    public NetworkVariable<int> HP = new NetworkVariable<int>(100); 
    public NetworkVariable<bool> HasPickedCard = new NetworkVariable<bool>(false);

    public void ResetForNewRound()
    {
        HasPickedCard.Value = false;
    }
}
