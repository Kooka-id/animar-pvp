using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EffectType { BuffAttack, BuffDefense, DebuffAttack, DebuffDefense, Heal }

[CreateAssetMenu(fileName = "NewActionCard", menuName = "Card System/Action Card")]
public class ActionCardData : ScriptableObject
{
    public string cardName;
    public Sprite cardSprite;
    public EffectType effectType;
    public float value; // misalnya 0.2 = 20%
}
