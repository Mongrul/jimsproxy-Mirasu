using HermesProxy.World.Enums;

namespace HermesProxy.World;

// JimsProxy: synthesized proficiency masks per class for SMSG_SET_PROFICIENCY.
//
// Vanilla 1.12 servers store armor / weapon proficiencies in a server-side
// bitmask and historically broadcast SMSG_SET_PROFICIENCY at session login.
// Twinstar Kronos (and likely other vmangos forks) skips that broadcast.
//
// The modern 1.14 Classic Era client's tooltip color logic — "render Mail
// in red when this class can't wear it" — falls back to "no restrictions"
// when neither SMSG_SET_PROFICIENCY nor a proficiency-spell bitmap arrives,
// so off-class armor and weapons render in white as if usable.
//
// Synthesizing SMSG_SET_PROFICIENCY at login (once class + level are known)
// puts the bitmask the modern client expects on the wire. Hook lives in
// UpdateHandler.cs where CurrentPlayerClass is set.
//
// Subclass-bit conventions match vanilla 1.12 wire format:
//   ItemSubClassArmor:  Cloth=1, Leather=2, Mail=3, Plate=4, Shield=6,
//                       Libram=7, Idol=8, Totem=9. Mask bit = 1 << subclass.
//   ItemSubClassWeapon: Axe1H=0, Axe2H=1, Bow=2, Gun=3, Mace1H=4, Mace2H=5,
//                       Polearm=6, Sword1H=7, Sword2H=8, Staff=10, Fist=13,
//                       Dagger=15, Thrown=16, Crossbow=18, Wand=19.
//                       Mask bit = 1 << subclass.
//
// Armor upgrades at level 40 are level-gated (Hunter / Shaman gain Mail,
// Warrior / Paladin gain Plate). Caller re-emits when level crosses 40.
//
// Weapon proficiencies omit class-trainable additions (Warrior Polearms,
// etc.) — the synthesized mask reflects starting proficiency only. The
// modern client's tooltip color uses the bitmask we send authoritatively;
// for items the player has trained at a class trainer post-login, the
// hand-trained mask gets re-emitted via the natural SMSG_SET_PROFICIENCY
// path on the legacy wire (vmangos sends it on each AddProficiency call).
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

    public static uint GetArmorMask(Class classId, uint level)
    {
        uint mask = 0;
        switch (classId)
        {
            case Class.Warrior:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorMail) | Bit(ArmorShield);
                if (level >= 40) mask |= Bit(ArmorPlate);
                break;

            case Class.Paladin:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorMail) | Bit(ArmorShield) | Bit(ArmorLibram);
                if (level >= 40) mask |= Bit(ArmorPlate);
                break;

            case Class.Hunter:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather);
                if (level >= 40) mask |= Bit(ArmorMail);
                break;

            case Class.Rogue:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather);
                break;

            case Class.Priest:
                mask = Bit(ArmorCloth);
                break;

            case Class.Shaman:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorShield) | Bit(ArmorTotem);
                if (level >= 40) mask |= Bit(ArmorMail);
                break;

            case Class.Mage:
                mask = Bit(ArmorCloth);
                break;

            case Class.Warlock:
                mask = Bit(ArmorCloth);
                break;

            case Class.Druid:
                mask = Bit(ArmorCloth) | Bit(ArmorLeather) | Bit(ArmorIdol);
                break;
        }
        return mask;
    }

    public static uint GetWeaponMask(Class classId, uint level)
    {
        uint mask = 0;
        switch (classId)
        {
            case Class.Warrior:
                mask = Bit(WpnAxe1H) | Bit(WpnAxe2H) | Bit(WpnBow) | Bit(WpnGun)
                     | Bit(WpnMace1H) | Bit(WpnMace2H) | Bit(WpnPolearm)
                     | Bit(WpnSword1H) | Bit(WpnSword2H) | Bit(WpnStaff)
                     | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnThrown) | Bit(WpnCrossbow);
                break;

            case Class.Paladin:
                mask = Bit(WpnAxe1H) | Bit(WpnAxe2H)
                     | Bit(WpnMace1H) | Bit(WpnMace2H) | Bit(WpnPolearm)
                     | Bit(WpnSword1H) | Bit(WpnSword2H);
                break;

            case Class.Hunter:
                mask = Bit(WpnAxe1H) | Bit(WpnAxe2H) | Bit(WpnBow) | Bit(WpnGun)
                     | Bit(WpnPolearm) | Bit(WpnSword1H) | Bit(WpnSword2H)
                     | Bit(WpnStaff) | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnCrossbow);
                break;

            case Class.Rogue:
                mask = Bit(WpnBow) | Bit(WpnGun) | Bit(WpnMace1H) | Bit(WpnSword1H)
                     | Bit(WpnFist) | Bit(WpnDagger) | Bit(WpnThrown) | Bit(WpnCrossbow);
                break;

            case Class.Priest:
                mask = Bit(WpnMace1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);
                break;

            case Class.Shaman:
                mask = Bit(WpnAxe1H) | Bit(WpnAxe2H) | Bit(WpnMace1H) | Bit(WpnMace2H)
                     | Bit(WpnStaff) | Bit(WpnFist) | Bit(WpnDagger);
                break;

            case Class.Mage:
                mask = Bit(WpnSword1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);
                break;

            case Class.Warlock:
                mask = Bit(WpnSword1H) | Bit(WpnStaff) | Bit(WpnDagger) | Bit(WpnWand);
                break;

            case Class.Druid:
                mask = Bit(WpnMace1H) | Bit(WpnMace2H) | Bit(WpnPolearm)
                     | Bit(WpnStaff) | Bit(WpnFist) | Bit(WpnDagger);
                break;
        }
        return mask;
    }

    public static uint ArmorClass => ItemClassArmor;
    public static uint WeaponClass => ItemClassWeapon;

    private static uint Bit(int subclass) => 1u << subclass;
}
