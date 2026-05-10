local ADDON_NAME, namespace = ...

-- JimsPlus TooltipFix: recolor armor / weapon type lines on tooltips when the
-- player's class can't use the item.
--
-- Why this is an addon: the 1.14 Classic Era client determines tooltip armor /
-- weapon "you can't wear this" coloring from a hardcoded class-proficiency
-- table baked into the client. Three wire paths from the proxy (legacy
-- SMSG_SET_PROFICIENCY, post-login SMSG_LEARNED_SPELLS, augmented initial
-- SMSG_SEND_KNOWN_SPELLS) all confirmed delivered to the modern client and
-- ALL ignored for tooltip color. The signal lives somewhere we can't reach
-- from the wire — but we CAN do the recolor ourselves in Lua.
--
-- Approach:
--   1. Static class-proficiency table (matches ProficiencyData.cs in proxy).
--   2. On tooltip-set-item, read itemClassID + itemSubClassID via GetItemInfo.
--   3. If player can't use that class+subclass, walk tooltip lines, find the
--      one carrying the subclass name (e.g. "Mail" / "Plate" / "Polearms"),
--      recolor it red.

local TooltipFix = {}
namespace.TooltipFix = TooltipFix

-- Item.ItemClass enum (WoW global): 2 = Armor, 4 = Weapon.
local ITEM_CLASS_ARMOR = 2
local ITEM_CLASS_WEAPON = 4

-- ItemSubClassArmor values.
local ARMOR_SUB = {
    CLOTH = 1, LEATHER = 2, MAIL = 3, PLATE = 4,
    SHIELD = 6, LIBRAM = 7, IDOL = 8, TOTEM = 9,
}

-- ItemSubClassWeapon values.
local WPN_SUB = {
    AXE_1H = 0, AXE_2H = 1, BOW = 2, GUN = 3,
    MACE_1H = 4, MACE_2H = 5, POLEARM = 6, SWORD_1H = 7, SWORD_2H = 8,
    STAFF = 10, FIST = 13, DAGGER = 15, THROWN = 16,
    CROSSBOW = 18, WAND = 19,
}

-- Per-class allowed subclass sets. Mirrors the proxy's ProficiencyData
-- baselines. Includes level-40 armor upgrades (Hunter/Shaman → Mail,
-- Warrior/Paladin → Plate) — a 39-paladin will see Plate red, a 40+
-- paladin who's trained will see it normal.
local CLASS_PROFICIENCY = {
    WARRIOR = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true, [ARMOR_SUB.MAIL]=true, [ARMOR_SUB.SHIELD]=true },
        armorAtLevel40 = { [ARMOR_SUB.PLATE]=true },
        weapon = { [WPN_SUB.AXE_1H]=true, [WPN_SUB.AXE_2H]=true, [WPN_SUB.BOW]=true, [WPN_SUB.GUN]=true,
                   [WPN_SUB.MACE_1H]=true, [WPN_SUB.MACE_2H]=true, [WPN_SUB.POLEARM]=true,
                   [WPN_SUB.SWORD_1H]=true, [WPN_SUB.SWORD_2H]=true, [WPN_SUB.STAFF]=true,
                   [WPN_SUB.FIST]=true, [WPN_SUB.DAGGER]=true, [WPN_SUB.THROWN]=true, [WPN_SUB.CROSSBOW]=true },
    },
    PALADIN = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true, [ARMOR_SUB.MAIL]=true, [ARMOR_SUB.SHIELD]=true, [ARMOR_SUB.LIBRAM]=true },
        armorAtLevel40 = { [ARMOR_SUB.PLATE]=true },
        weapon = { [WPN_SUB.AXE_1H]=true, [WPN_SUB.AXE_2H]=true, [WPN_SUB.MACE_1H]=true, [WPN_SUB.MACE_2H]=true,
                   [WPN_SUB.POLEARM]=true, [WPN_SUB.SWORD_1H]=true, [WPN_SUB.SWORD_2H]=true },
    },
    HUNTER = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true },
        armorAtLevel40 = { [ARMOR_SUB.MAIL]=true },
        weapon = { [WPN_SUB.AXE_1H]=true, [WPN_SUB.AXE_2H]=true, [WPN_SUB.BOW]=true, [WPN_SUB.GUN]=true,
                   [WPN_SUB.POLEARM]=true, [WPN_SUB.SWORD_1H]=true, [WPN_SUB.SWORD_2H]=true,
                   [WPN_SUB.STAFF]=true, [WPN_SUB.FIST]=true, [WPN_SUB.DAGGER]=true,
                   [WPN_SUB.THROWN]=true, [WPN_SUB.CROSSBOW]=true },
    },
    ROGUE = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true },
        weapon = { [WPN_SUB.BOW]=true, [WPN_SUB.GUN]=true, [WPN_SUB.MACE_1H]=true, [WPN_SUB.SWORD_1H]=true,
                   [WPN_SUB.FIST]=true, [WPN_SUB.DAGGER]=true, [WPN_SUB.THROWN]=true, [WPN_SUB.CROSSBOW]=true },
    },
    PRIEST = {
        armor = { [ARMOR_SUB.CLOTH]=true },
        weapon = { [WPN_SUB.MACE_1H]=true, [WPN_SUB.STAFF]=true, [WPN_SUB.DAGGER]=true, [WPN_SUB.WAND]=true },
    },
    SHAMAN = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true, [ARMOR_SUB.SHIELD]=true, [ARMOR_SUB.TOTEM]=true },
        armorAtLevel40 = { [ARMOR_SUB.MAIL]=true },
        weapon = { [WPN_SUB.AXE_1H]=true, [WPN_SUB.AXE_2H]=true, [WPN_SUB.MACE_1H]=true, [WPN_SUB.MACE_2H]=true,
                   [WPN_SUB.STAFF]=true, [WPN_SUB.FIST]=true, [WPN_SUB.DAGGER]=true },
    },
    MAGE = {
        armor = { [ARMOR_SUB.CLOTH]=true },
        weapon = { [WPN_SUB.SWORD_1H]=true, [WPN_SUB.STAFF]=true, [WPN_SUB.DAGGER]=true, [WPN_SUB.WAND]=true },
    },
    WARLOCK = {
        armor = { [ARMOR_SUB.CLOTH]=true },
        weapon = { [WPN_SUB.SWORD_1H]=true, [WPN_SUB.STAFF]=true, [WPN_SUB.DAGGER]=true, [WPN_SUB.WAND]=true },
    },
    DRUID = {
        armor = { [ARMOR_SUB.CLOTH]=true, [ARMOR_SUB.LEATHER]=true, [ARMOR_SUB.IDOL]=true },
        weapon = { [WPN_SUB.MACE_1H]=true, [WPN_SUB.MACE_2H]=true, [WPN_SUB.POLEARM]=true,
                   [WPN_SUB.STAFF]=true, [WPN_SUB.FIST]=true, [WPN_SUB.DAGGER]=true },
    },
}

-- Localized name of each subclass as it appears on tooltips. Used to find
-- the line we need to recolor. WoW exposes these as global strings — fall
-- back to English if a global is missing on this client/locale.
local function GetArmorSubclassName(subclassId)
    if subclassId == ARMOR_SUB.CLOTH   then return _G.GetItemSubClassInfo and GetItemSubClassInfo(ITEM_CLASS_ARMOR, ARMOR_SUB.CLOTH)   or "Cloth" end
    if subclassId == ARMOR_SUB.LEATHER then return _G.GetItemSubClassInfo and GetItemSubClassInfo(ITEM_CLASS_ARMOR, ARMOR_SUB.LEATHER) or "Leather" end
    if subclassId == ARMOR_SUB.MAIL    then return _G.GetItemSubClassInfo and GetItemSubClassInfo(ITEM_CLASS_ARMOR, ARMOR_SUB.MAIL)    or "Mail" end
    if subclassId == ARMOR_SUB.PLATE   then return _G.GetItemSubClassInfo and GetItemSubClassInfo(ITEM_CLASS_ARMOR, ARMOR_SUB.PLATE)   or "Plate" end
    if subclassId == ARMOR_SUB.SHIELD  then return _G.GetItemSubClassInfo and GetItemSubClassInfo(ITEM_CLASS_ARMOR, ARMOR_SUB.SHIELD)  or "Shields" end
    return nil
end

local function GetWeaponSubclassName(subclassId)
    if not _G.GetItemSubClassInfo then return nil end
    return GetItemSubClassInfo(ITEM_CLASS_WEAPON, subclassId)
end

local function PlayerCanUse(itemClassId, itemSubClassId)
    local _, classFile = UnitClass("player")
    local prof = CLASS_PROFICIENCY[classFile]
    if not prof then return true end -- unknown class: don't recolor

    local level = UnitLevel("player") or 1

    if itemClassId == ITEM_CLASS_ARMOR then
        if prof.armor and prof.armor[itemSubClassId] then return true end
        if level >= 40 and prof.armorAtLevel40 and prof.armorAtLevel40[itemSubClassId] then return true end
        return false
    elseif itemClassId == ITEM_CLASS_WEAPON then
        if prof.weapon and prof.weapon[itemSubClassId] then return true end
        return false
    end

    return true -- non-armor/weapon items: don't recolor
end

-- Walks tooltip lines looking for the one that exactly matches the item's
-- subclass name (e.g. "Mail" / "Plate" / "Polearms") and recolors it red.
-- Tooltip lines are FontString objects; SetTextColor is the standard recolor.
local function RecolorTypeLineRed(tooltip, subclassName)
    if not subclassName or subclassName == "" then return end

    local tooltipName = tooltip:GetName()
    if not tooltipName then return end

    -- Tooltips have up to ~30 lines; both Left and Right columns. The
    -- subclass appears on the right side of the slot/type row in retail
    -- (e.g. "Feet  ...  Mail"), and stand-alone on the left for shields.
    local numLines = tooltip:NumLines()
    for i = 1, numLines do
        local rightFs = _G[tooltipName .. "TextRight" .. i]
        if rightFs and rightFs:GetText() == subclassName then
            rightFs:SetTextColor(1.0, 0.1, 0.1)
        end
        local leftFs = _G[tooltipName .. "TextLeft" .. i]
        if leftFs and leftFs:GetText() == subclassName then
            leftFs:SetTextColor(1.0, 0.1, 0.1)
        end
    end
end

local function OnTooltipItem(tooltip)
    if not (namespace.db and namespace.db.tooltipFix) then return end
    local _, link = tooltip:GetItem()
    if not link then return end

    local _, _, _, _, _, _, _, _, _, _, _, classId, subClassId = GetItemInfo(link)
    if not classId or not subClassId then return end

    if PlayerCanUse(classId, subClassId) then return end

    local subclassName
    if classId == ITEM_CLASS_ARMOR then
        subclassName = GetArmorSubclassName(subClassId)
    elseif classId == ITEM_CLASS_WEAPON then
        subclassName = GetWeaponSubclassName(subClassId)
    end

    RecolorTypeLineRed(tooltip, subclassName)
end

local function HookTooltips()
    -- Cover both the main GameTooltip and the comparison shopping tooltips.
    -- The 1.14 Classic Era client supports OnTooltipSetItem on these.
    local tooltips = {
        GameTooltip,
        ItemRefTooltip,
        ShoppingTooltip1,
        ShoppingTooltip2,
    }
    for _, tip in ipairs(tooltips) do
        if tip and tip.HookScript then
            tip:HookScript("OnTooltipSetItem", OnTooltipItem)
        end
    end
end

function TooltipFix:Init()
    if namespace.db.tooltipFix == nil then namespace.db.tooltipFix = true end
    if not namespace.db.tooltipFix then return end
    HookTooltips()
end

namespace:RegisterModule("TooltipFix", function() TooltipFix:Init() end)

-- Run on PLAYER_LOGIN so GameTooltip and friends exist.
local f = CreateFrame("Frame")
f:RegisterEvent("PLAYER_LOGIN")
f:SetScript("OnEvent", function()
    -- ADDON_LOADED may not have populated namespace.db yet; pull from saved.
    JimsPlusDB = JimsPlusDB or {}
    namespace.db = JimsPlusDB
    if JimsPlusDB.tooltipFix == nil then JimsPlusDB.tooltipFix = true end
    if JimsPlusDB.tooltipFix then HookTooltips() end
end)
