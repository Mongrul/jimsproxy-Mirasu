using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World;

// JimsProxy: synthesized proficiency masks per class for SMSG_SET_PROFICIENCY.
//
// Vanilla 1.12 servers store armor / weapon proficiencies in a server-side
// bitmask and historically broadcast SMSG_SET_PROFICIENCY at session login.
// Twinstar Kronos (and likely other vmangos forks) skips that broadcast
// (confirmed in tester JSONL: zero SMSG_SET_PROFICIENCY events in an entire
// session).
//
// The modern 1.14 Classic Era client's tooltip color logic — "render Mail
// in red when this class can't wear it" — falls back to "no restrictions"
// when the bitmask never arrives, so off-class armor and weapons render in
// white and the equip-compare panel suppresses cross-class deltas.
//
// Synthesizing the bitmask in the proxy puts what the modern client expects
// on the wire. The mask is the union of:
//   1) A class baseline (level-1 starting proficiencies, no trained additions)
//   2) Bits set by proficiency-granting spells the player KNOWS, derived from
//      CurrentPlayerKnownSpells. Trainer-learned proficiencies (Plate at 40
//      paladin/warrior, Mail at 40 hunter/shaman, Polearms warrior, etc.)
//      arrive as SMSG_LEARNED_SPELL → added to the known set → next call to
//      ComputeMasks picks them up automatically.
//
// Re-emit on every SMSG_LEARNED_SPELL; emission is skipped when the
// recomputed mask matches the last-sent mask.
//
// If the legacy server ever DOES send a real SMSG_SET_PROFICIENCY (e.g. when
// a class trainer grants a proficiency on a server that emits it), the
// natural HandleSetProficiency forwarder runs and overrides our synthesized
// mask — that's the correct precedence.
//
// Subclass-bit conventions match vanilla 1.12 wire format:
//   ItemSubClassArmor:  Cloth=1, Leather=2, Mail=3, Plate=4, Shield=6,
//                       Libram=7, Idol=8, Totem=9. Mask bit = 1 << subclass.
//   ItemSubClassWeapon: Axe1H=0, Axe2H=1, Bow=2, Gun=3, Mace1H=4, Mace2H=5,
//                       Polearm=6, Sword1H=7, Sword2H=8, Staff=10, Fist=13,
//                       Dagger=15, Thrown=16, Crossbow=18, Wand=19.
//                       Mask bit = 1 << subclass.
public static class ProficiencyData
{
    // Subclass IDs for armor (bit positions).
    private const int ArmorCloth   = 1;
    private const int ArmorLeather = 2;
    private const int ArmorMail    = 3;
    private const int ArmorPlate   = 4;
    private const int ArmorShield  = 6;
    private const int ArmorLibram  = 7;
    private const int ArmorIdol    = 8;
    private const int ArmorTotem   = 9;

    // Subclass IDs for weapons (bit positions).
    private const int WpnAxe1H    = 0;
    private const int WpnAxe2H    = 1;
    private const int WpnBow      = 2;
    private const int WpnGun      = 3;
    private const int WpnMace1H   = 4;
    private const int WpnMace2H   = 5;
    private const int WpnPolearm  = 6;
    private const int WpnSword1H  = 7;
    private const int WpnSword2H  = 8;
    private const int WpnStaff    = 10;
    private const int WpnFist     = 13;
    private const int WpnDagger   = 15;
    private const int WpnThrown   = 16;
    private const int WpnCrossbow = 18;
    private const int WpnWand     = 19;

    private const uint ItemClassArmor = 2;
    private const uint ItemClassWeapon = 4;

    public static uint ArmorClass => ItemClassArmor;
    public static uint WeaponClass => ItemClassWeapon;

    // Vanilla 1.12 proficiency-granting passive spell IDs. Players who know
    // the spell have the corresponding subclass bit set in their mask. Source:
    // mangos / vmangos `SkillLineAbility.dbc`-derived spell list.
    private static readonly Dictionary<uint, int> ArmorProficiencySpells = new()
    {
        { 9077,  ArmorCloth   },
        { 9078,  ArmorLeather },
        { 8737,  ArmorMail    },
        { 750,   ArmorPlate   },
        { 9116,  ArmorShield  },
        // Libram / Idol / Totem are class-baseline only (no separate spell).
    };

    private static readonly Dictionary<uint, int> WeaponProficiencySpells = new()
    {
        { 196,   WpnAxe1H    },
        { 197,   WpnAxe2H    },
        { 264,   WpnBow      },
        { 266,   WpnGun      },
        { 198,   WpnMace1H   },
        { 199,   WpnMace2H   },
        { 200,   WpnPolearm  },
        { 201,   WpnSword1H  },
        { 202,   WpnSword2H  },
        { 227,   WpnStaff    },
        { 15590, WpnFist     },
        { 1180,  WpnDagger   },
        { 2567,  WpnThrown   },
        { 5011,  WpnCrossbow },
        { 5009,  WpnWand     },
    };

    // Class baseline = the proficiencies the class has at character creation
    // before visiting any trainer. Excludes the level-40 armor upgrades
    // (those arrive as SMSG_LEARNED_SPELL when trained at the trainer).
    private static uint GetClassBaselineArmor(Class classId)
    {
        switch (classId)
        {
            case Class.Warrior: return Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorMail) | Bit(ArmorShield);
            case Class.Paladin: return Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorMail) | Bit(ArmorShield) | Bit(ArmorLibram);
            case Class.Hunter:  return Bit(ArmorCloth) | Bit(ArmorLeather);
            case Class.Rogue:   return Bit(ArmorCloth) | Bit(ArmorLeather);
            case Class.Priest:  return Bit(ArmorCloth);
            case Class.Shaman:  return Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorShield) | Bit(ArmorTotem);
            case Class.Mage:    return Bit(ArmorCloth);
            case Class.Warlock: return Bit(ArmorCloth);
            case Class.Druid:   return Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorIdol);
            default:            return 0;
        }
    }

    private static uint GetClassBaselineWeapon(Class classId)
    {
        switch (classId)
        {
            case Class.Warrior:
                // Warrior starts with Axe/Mace/Sword 1H + Bow / Gun / Crossbow + Throw + Fist + Daggers.
                // 2H Axe/Mace/Sword and Polearms are TRAINED at the warrior trainer.
                return Bit(WpnAxe1H) | Bit(WpnBow) | Bit(WpnGun)
                     | Bit(WpnMace1H) | Bit(WpnSword1H)
                     | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnThrown) | Bit(WpnCrossbow);

            case Class.Paladin:
                // Paladin starts with Mace 1H + Sword 1H. Other weapons trained.
                return Bit(WpnMace1H) | Bit(WpnSword1H);

            case Class.Hunter:
                // Hunter starts with Axe 1H + Sword 1H + Dagger + Bow + Gun + Crossbow + Fist + Staff + Polearm + Throw.
                return Bit(WpnAxe1H) | Bit(WpnSword1H) | Bit(WpnBow) | Bit(WpnGun)
                     | Bit(WpnDagger) | Bit(WpnFist) | Bit(WpnStaff) | Bit(WpnPolearm)
                     | Bit(WpnThrown) | Bit(WpnCrossbow);

            case Class.Rogue:
                // Rogue starts with Bow + Crossbow + Gun + Mace 1H + Sword 1H + Fist + Dagger + Throw.
                return Bit(WpnBow) | Bit(WpnGun) | Bit(WpnMace1H) | Bit(WpnSword1H)
                     | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnThrown) | Bit(WpnCrossbow);

            case Class.Priest:
                return Bit(WpnMace1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);

            case Class.Shaman:
                // Shaman starts with Mace 1H + Staff + Dagger + Fist. 2H Mace / Axes trained.
                return Bit(WpnMace1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnFist);

            case Class.Mage:
                return Bit(WpnSword1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);

            case Class.Warlock:
                return Bit(WpnSword1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);

            case Class.Druid:
                return Bit(WpnMace1H) | Bit(WpnMace2H) | Bit(WpnStaff)
                     | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnPolearm);

            default:
                return 0;
        }
    }

    // Compute armor + weapon proficiency masks from class baseline OR'd with
    // proficiency-spell bits derived from the player's known-spells set.
    public static (uint Armor, uint Weapon) ComputeMasks(Class classId, ICollection<uint> knownSpells)
    {
        uint armor = GetClassBaselineArmor(classId);
        uint weapon = GetClassBaselineWeapon(classId);

        foreach (var spellId in knownSpells)
        {
            if (ArmorProficiencySpells.TryGetValue(spellId, out int armorBit))
                armor |= 1u << armorBit;
            if (WeaponProficiencySpells.TryGetValue(spellId, out int weaponBit))
                weapon |= 1u << weaponBit;
        }

        return (armor, weapon);
    }

    public static bool IsProficiencySpell(uint spellId)
    {
        return ArmorProficiencySpells.ContainsKey(spellId)
            || WeaponProficiencySpells.ContainsKey(spellId);
    }

    private static uint Bit(int subclass) => 1u << subclass;
}
