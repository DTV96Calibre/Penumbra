using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Logging;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Penumbra.GameData.Data;

internal class GamePathParser : IGamePathParser
{
    public GameObjectInfo GetFileInfo(string path)
    {
        path = path.ToLowerInvariant().Replace('\\', '/');

        var (fileType, objectType, match) = ParseGamePath(path);
        if (match is not { Success: true })
            return new GameObjectInfo
            {
                FileType   = fileType,
                ObjectType = objectType,
            };

        try
        {
            var groups = match.Groups;
            switch (objectType)
            {
                case ObjectType.Accessory: return HandleEquipment(fileType, groups);
                case ObjectType.Equipment: return HandleEquipment(fileType, groups);
                case ObjectType.Weapon:    return HandleWeapon(fileType, groups);
                case ObjectType.Map:       return HandleMap(fileType, groups);
                case ObjectType.Monster:   return HandleMonster(fileType, groups);
                case ObjectType.DemiHuman: return HandleDemiHuman(fileType, groups);
                case ObjectType.Character: return HandleCustomization(fileType, groups);
                case ObjectType.Icon:      return HandleIcon(fileType, groups);
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"Could not parse {path}:\n{e}");
        }

        return new GameObjectInfo
        {
            FileType   = fileType,
            ObjectType = objectType,
        };
    }

    public string VfxToKey(string path)
    {
        var match = _vfxRegexTmb.Match(path);
        if (match.Success)
            return match.Groups["key"].Value.ToLowerInvariant();

        match = _vfxRegexPap.Match(path);
        return match.Success ? match.Groups["key"].Value.ToLowerInvariant() : string.Empty;
    }

    private const string CharacterFolder = "chara";
    private const string EquipmentFolder = "equipment";
    private const string PlayerFolder    = "human";
    private const string WeaponFolder    = "weapon";
    private const string AccessoryFolder = "accessory";
    private const string DemiHumanFolder = "demihuman";
    private const string MonsterFolder   = "monster";
    private const string CommonFolder    = "common";
    private const string UiFolder        = "ui";
    private const string IconFolder      = "icon";
    private const string LoadingFolder   = "loadingimage";
    private const string MapFolder       = "map";
    private const string InterfaceFolder = "uld";
    private const string FontFolder      = "font";
    private const string HousingFolder   = "hou";
    private const string VfxFolder       = "vfx";
    private const string WorldFolder1    = "bgcommon";
    private const string WorldFolder2    = "bg";

    public ObjectType PathToObjectType(string path)
    {
        if (path.Length == 0)
            return ObjectType.Unknown;

        var folders = path.Split('/');
        if (folders.Length < 2)
            return ObjectType.Unknown;

        return folders[0] switch
        {
            CharacterFolder => folders[1] switch
            {
                EquipmentFolder => ObjectType.Equipment,
                AccessoryFolder => ObjectType.Accessory,
                WeaponFolder    => ObjectType.Weapon,
                PlayerFolder    => ObjectType.Character,
                DemiHumanFolder => ObjectType.DemiHuman,
                MonsterFolder   => ObjectType.Monster,
                CommonFolder    => ObjectType.Character,
                _               => ObjectType.Unknown,
            },
            UiFolder => folders[1] switch
            {
                IconFolder      => ObjectType.Icon,
                LoadingFolder   => ObjectType.LoadingScreen,
                MapFolder       => ObjectType.Map,
                InterfaceFolder => ObjectType.Interface,
                _               => ObjectType.Unknown,
            },
            CommonFolder => folders[1] switch
            {
                FontFolder => ObjectType.Font,
                _          => ObjectType.Unknown,
            },
            HousingFolder => ObjectType.Housing,
            WorldFolder1 => folders[1] switch
            {
                HousingFolder => ObjectType.Housing,
                _             => ObjectType.World,
            },
            WorldFolder2 => ObjectType.World,
            VfxFolder    => ObjectType.Vfx,
            _            => ObjectType.Unknown,
        };
    }

    private (FileType, ObjectType, Match?) ParseGamePath(string path)
    {
        if (!Names.ExtensionToFileType.TryGetValue(Path.GetExtension(path), out var fileType))
            fileType = FileType.Unknown;

        var objectType = PathToObjectType(path);

        static Match TestCharacterTextures(string path)
        {
            var regexes = new Regex[]
            {
                GamePathManager.Character.Tex.Regex(),
                GamePathManager.Character.Tex.FolderRegex(),
                GamePathManager.Character.Tex.SkinRegex(),
                GamePathManager.Character.Tex.CatchlightRegex(),
                GamePathManager.Character.Tex.DecalRegex(),
            };
            foreach (var regex in regexes)
            {
                var match = regex.Match(path);
                if (match.Success)
                    return match;
            }

            return Match.Empty;
        }

        var match = (fileType, objectType) switch
        {
            (FileType.Font, ObjectType.Font)          => GamePathManager.Font.Regex().Match(path),
            (FileType.Imc, ObjectType.Weapon)         => GamePathManager.Weapon.Imc.Regex().Match(path),
            (FileType.Imc, ObjectType.Monster)        => GamePathManager.Monster.Imc.Regex().Match(path),
            (FileType.Imc, ObjectType.DemiHuman)      => GamePathManager.DemiHuman.Imc.Regex().Match(path),
            (FileType.Imc, ObjectType.Equipment)      => GamePathManager.Equipment.Imc.Regex().Match(path),
            (FileType.Imc, ObjectType.Accessory)      => GamePathManager.Accessory.Imc.Regex().Match(path),
            (FileType.Model, ObjectType.Weapon)       => GamePathManager.Weapon.Mdl.Regex().Match(path),
            (FileType.Model, ObjectType.Monster)      => GamePathManager.Monster.Mdl.Regex().Match(path),
            (FileType.Model, ObjectType.DemiHuman)    => GamePathManager.DemiHuman.Mdl.Regex().Match(path),
            (FileType.Model, ObjectType.Equipment)    => GamePathManager.Equipment.Mdl.Regex().Match(path),
            (FileType.Model, ObjectType.Accessory)    => GamePathManager.Accessory.Mdl.Regex().Match(path),
            (FileType.Model, ObjectType.Character)    => GamePathManager.Character.Mdl.Regex().Match(path),
            (FileType.Material, ObjectType.Weapon)    => GamePathManager.Weapon.Mtrl.Regex().Match(path),
            (FileType.Material, ObjectType.Monster)   => GamePathManager.Monster.Mtrl.Regex().Match(path),
            (FileType.Material, ObjectType.DemiHuman) => GamePathManager.DemiHuman.Mtrl.Regex().Match(path),
            (FileType.Material, ObjectType.Equipment) => GamePathManager.Equipment.Mtrl.Regex().Match(path),
            (FileType.Material, ObjectType.Accessory) => GamePathManager.Accessory.Mtrl.Regex().Match(path),
            (FileType.Material, ObjectType.Character) => GamePathManager.Character.Mtrl.Regex().Match(path),
            (FileType.Texture, ObjectType.Weapon)     => GamePathManager.Weapon.Tex.Regex().Match(path),
            (FileType.Texture, ObjectType.Monster)    => GamePathManager.Monster.Tex.Regex().Match(path),
            (FileType.Texture, ObjectType.DemiHuman)  => GamePathManager.DemiHuman.Tex.Regex().Match(path),
            (FileType.Texture, ObjectType.Equipment)  => GamePathManager.Equipment.Tex.Regex().Match(path),
            (FileType.Texture, ObjectType.Accessory)  => GamePathManager.Accessory.Tex.Regex().Match(path),
            (FileType.Texture, ObjectType.Character)  => TestCharacterTextures(path),
            (FileType.Texture, ObjectType.Icon)       => GamePathManager.Icon.Regex().Match(path),
            (FileType.Texture, ObjectType.Map)        => GamePathManager.Map.Regex().Match(path),
            _                                         => Match.Empty,
        };

        return (fileType, objectType, match.Success ? match : null);
    }

    private static GameObjectInfo HandleEquipment(FileType fileType, GroupCollection groups)
    {
        var setId = ushort.Parse(groups["id"].Value);
        if (fileType == FileType.Imc)
            return GameObjectInfo.Equipment(fileType, setId);

        var gr   = Names.GenderRaceFromCode(groups["race"].Value);
        var slot = Names.SuffixToEquipSlot[groups["slot"].Value];
        if (fileType == FileType.Model)
            return GameObjectInfo.Equipment(fileType, setId, gr, slot);

        var variant = byte.Parse(groups["variant"].Value);
        return GameObjectInfo.Equipment(fileType, setId, gr, slot, variant);
    }

    private static GameObjectInfo HandleWeapon(FileType fileType, GroupCollection groups)
    {
        var weaponId = ushort.Parse(groups["weapon"].Value);
        var setId    = ushort.Parse(groups["id"].Value);
        if (fileType is FileType.Imc or FileType.Model)
            return GameObjectInfo.Weapon(fileType, setId, weaponId);

        var variant = byte.Parse(groups["variant"].Value);
        return GameObjectInfo.Weapon(fileType, setId, weaponId, variant);
    }

    private static GameObjectInfo HandleMonster(FileType fileType, GroupCollection groups)
    {
        var monsterId = ushort.Parse(groups["monster"].Value);
        var bodyId    = ushort.Parse(groups["id"].Value);
        if (fileType is FileType.Imc or FileType.Model)
            return GameObjectInfo.Monster(fileType, monsterId, bodyId);

        var variant = byte.Parse(groups["variant"].Value);
        return GameObjectInfo.Monster(fileType, monsterId, bodyId, variant);
    }

    private static GameObjectInfo HandleDemiHuman(FileType fileType, GroupCollection groups)
    {
        var demiHumanId = ushort.Parse(groups["id"].Value);
        var equipId     = ushort.Parse(groups["equip"].Value);
        if (fileType == FileType.Imc)
            return GameObjectInfo.DemiHuman(fileType, demiHumanId, equipId);

        var slot = Names.SuffixToEquipSlot[groups["slot"].Value];
        if (fileType == FileType.Model)
            return GameObjectInfo.DemiHuman(fileType, demiHumanId, equipId, slot);

        var variant = byte.Parse(groups["variant"].Value);
        return GameObjectInfo.DemiHuman(fileType, demiHumanId, equipId, slot, variant);
    }

    private static GameObjectInfo HandleCustomization(FileType fileType, GroupCollection groups)
    {
        if (groups["catchlight"].Success)
            return GameObjectInfo.Customization(fileType, CustomizationType.Iris);

        if (groups["skin"].Success)
            return GameObjectInfo.Customization(fileType, CustomizationType.Skin);

        var id = ushort.Parse(groups["id"].Value);
        if (groups["location"].Success)
        {
            var tmpType = groups["location"].Value == "face" ? CustomizationType.DecalFace
                : groups["location"].Value == "equip"        ? CustomizationType.DecalEquip : CustomizationType.Unknown;
            return GameObjectInfo.Customization(fileType, tmpType, id);
        }

        var gr       = Names.GenderRaceFromCode(groups["race"].Value);
        var bodySlot = Names.StringToBodySlot[groups["type"].Value];
        var type = groups["slot"].Success
            ? Names.SuffixToCustomizationType[groups["slot"].Value]
            : CustomizationType.Skin;
        if (fileType == FileType.Material)
        {
            var variant = groups["variant"].Success ? byte.Parse(groups["variant"].Value) : (byte)0;
            return GameObjectInfo.Customization(fileType, type, id, gr, bodySlot, variant);
        }

        return GameObjectInfo.Customization(fileType, type, id, gr, bodySlot);
    }

    private static GameObjectInfo HandleIcon(FileType fileType, GroupCollection groups)
    {
        var hq = groups["hq"].Success;
        var hr = groups["hr"].Success;
        var id = uint.Parse(groups["id"].Value);
        if (!groups["lang"].Success)
            return GameObjectInfo.Icon(fileType, id, hq, hr);

        var language = groups["lang"].Value switch
        {
            "en" => Dalamud.ClientLanguage.English,
            "ja" => Dalamud.ClientLanguage.Japanese,
            "de" => Dalamud.ClientLanguage.German,
            "fr" => Dalamud.ClientLanguage.French,
            _    => Dalamud.ClientLanguage.English,
        };
        return GameObjectInfo.Icon(fileType, id, hq, hr, language);
    }

    private static GameObjectInfo HandleMap(FileType fileType, GroupCollection groups)
    {
        var map     = Encoding.ASCII.GetBytes(groups["id"].Value);
        var variant = byte.Parse(groups["variant"].Value);
        if (groups["suffix"].Success)
        {
            var suffix = Encoding.ASCII.GetBytes(groups["suffix"].Value)[0];
            return GameObjectInfo.Map(fileType, map[0], map[1], map[2], map[3], variant, suffix);
        }

        return GameObjectInfo.Map(fileType, map[0], map[1], map[2], map[3], variant);
    }


    private readonly Regex _vfxRegexTmb = new(@"chara[\/]action[\/](?'key'[^\s]+?)\.tmb", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Regex _vfxRegexPap = new(@"chara[\/]human[\/]c0101[\/]animation[\/]a0001[\/][^\s]+?[\/](?'key'[^\s]+?)\.pap",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
