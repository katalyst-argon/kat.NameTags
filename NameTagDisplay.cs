using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace NametagMod
{
    public static class NametagDisplay
    {
        private const float SPRITE_LOCAL_SCALE = 0.18f;
        private const float SPRITE_PPU = 100f;

        private const float NAME_FONT_SIZE = 6.5f;
        private const float ID_FONT_SIZE = 5.0f;
        private const float FPS_FONT_SIZE = 5.0f;
        private const float CREATION_FONT_SIZE = 3.8f;

        private const float META_NAME_Y = 2.22f;
        private const float META_ID_Y = 1.62f;
        private const float META_FPS_Y = 1.12f;
        private const float META_CREATION_Y = 0.58f;

        private const float STEAM_NAME_Y = 2.62f;
        private const float STEAM_ID_Y = 2.02f;
        private const float STEAM_FPS_Y = 1.52f;
        private const float STEAM_CREATION_Y = 0.98f;

        private const float ICON_FPS_X = -1.2f;
        private const float PIPE_TEXT_X = -0.5f;
        private const float FPS_TEXT_X = 0.85f;

        private const string TMP_FONT_NAME = "LiberationSans SDF";

        private const float MOD_LABEL_FONT_SIZE = 3.5f;
        private const float MOD_LABEL_Y_ABOVE_NAME = 0.60f;
        private const int MOD_LINE_MAX_CHARS = 48;

        private static readonly Color MOD_LABEL_COLOR = new Color(1.00f, 0.30f, 0.30f);

        private static readonly HashSet<string> NATIVE_GT_KEYS = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "didTutorial",
            "color", "hat", "face", "badge",
            "leftHand", "rightHand", "lHand", "rHand",
            "Modded", "modded",
            "groupMapJoin", "groupMapJoinType",
            "inRoom", "InRoom",
            "isInQueue", "queue", "gameMode",
            "allowedInRoom",
            "playerCount", "playerCountMax",
            "roomCategory", "defaultGameMode",
            "currentQueue",
            "version", "Version",
            "voice",
            "CanSeeItems", "canSeeItems",
            "tagsGiven", "TagsGiven",
            "TagsReceived", "tagsReceived",
            "frozen", "Frozen",
        };

        private static readonly string iconsPath = Path.Combine(Application.dataPath, "..", "Mods", "PlatformIcons");
        private static readonly string pcIconPath = Path.Combine(iconsPath, "steam.png");
        private static readonly string standaloneIconPath = Path.Combine(iconsPath, "meta.png");

        private const string PC_ICON_URL = "https://i.ibb.co/DfLfrw0T/steam.png";
        private const string STANDALONE_ICON_URL = "https://i.ibb.co/RG6C1s1s/meta.png";

        private static Texture2D pcIcon = null;
        private static Texture2D standaloneIcon = null;
        private static Sprite pcSprite = null;
        private static Sprite standaloneSprite = null;
        private static bool iconsLoaded = false;
        private static bool downloadAttempted = false;

        private static FieldInfo fpsField;
        private static FieldInfo cosmeticsAllowedField;
        private static FieldInfo creatorField;
        private static bool reflectionReady = false;

        private static TMP_FontAsset sharedFont;

        private static readonly Dictionary<string, string> creationDateCache = new Dictionary<string, string>(32);

        private class TagData
        {
            public GameObject root;
            public SpriteRenderer spriteRenderer;
            public TextMeshPro nameText;
            public TextMeshPro idText;
            public TextMeshPro pipeText;
            public TextMeshPro fpsText;
            public TextMeshPro creationText;
            public TextMeshPro modText;

            public string cachedName;
            public string cachedId;
            public string cachedPlatform;
            public int cachedFPS = -1;
            public string cachedCreation;
            public string cachedModLine = null;
            public float modLastCheck = -99f;

            public GameObject auraGO = null;
            public ParticleSystem auraPS = null;
            public Color cachedAuraColor = Color.clear;

            public IEnumerable<TextMeshPro> AllLabels()
            {
                if (nameText != null) yield return nameText;
                if (idText != null) yield return idText;
                if (pipeText != null) yield return pipeText;
                if (fpsText != null) yield return fpsText;
                if (creationText != null) yield return creationText;
                if (modText != null) yield return modText;
            }
        }

        private static readonly Dictionary<VRRig, TagData> tags = new Dictionary<VRRig, TagData>(32);
        private static readonly List<VRRig> remove = new List<VRRig>(16);

        private static float _lastSize = -1f;
        private static float _lastHeight = -1f;
        private static string _lastColorHex = null;
        private static float _lastThickness = -1f;
        private static float _lastQuality = -1f;

        public static void Init()
        {
            InitReflection();
            LoadFont();
            LoadIcons();
        }

        private static void InitReflection()
        {
            if (reflectionReady) return;
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            fpsField = typeof(VRRig).GetField("fps", flags);
            cosmeticsAllowedField = typeof(VRRig).GetField("concatStringOfCosmeticsAllowed", flags);
            creatorField = typeof(VRRig).GetField("creator", flags);
            reflectionReady = true;
        }

        private static void LoadFont()
        {
            sharedFont = Resources.Load<TMP_FontAsset>(TMP_FONT_NAME);
            if (sharedFont == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null && all.Length > 0) sharedFont = all[0];
            }
        }

        private static void LoadIcons()
        {
            if (iconsLoaded) return;
            try
            {
                if (!Directory.Exists(iconsPath))
                    Directory.CreateDirectory(iconsPath);

                if (File.Exists(pcIconPath))
                {
                    pcIcon = new Texture2D(2, 2);
                    pcIcon.LoadImage(File.ReadAllBytes(pcIconPath));
                    pcSprite = MakeSprite(pcIcon);
                }
                else if (!downloadAttempted)
                    _ = DownloadIconAsync(PC_ICON_URL, pcIconPath, isPc: true);

                if (File.Exists(standaloneIconPath))
                {
                    standaloneIcon = new Texture2D(2, 2);
                    standaloneIcon.LoadImage(File.ReadAllBytes(standaloneIconPath));
                    standaloneSprite = MakeSprite(standaloneIcon);
                }
                else if (!downloadAttempted)
                    _ = DownloadIconAsync(STANDALONE_ICON_URL, standaloneIconPath, isPc: false);

                downloadAttempted = true;
                iconsLoaded = true;
            }
            catch (System.Exception ex)
            {
                NametagPlugin.Log.LogError($"[NametagMod] LoadIcons: {ex.Message}");
            }
        }

        private static async Task DownloadIconAsync(string url, string savePath, bool isPc)
        {
            try
            {
                using (var www = UnityWebRequestTexture.GetTexture(url))
                {
                    await www.SendWebRequest();
                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(www);
                        File.WriteAllBytes(savePath, tex.EncodeToPNG());

                        if (isPc) { pcIcon = tex; pcSprite = MakeSprite(tex); }
                        else { standaloneIcon = tex; standaloneSprite = MakeSprite(tex); }
                    }
                    else
                        NametagPlugin.Log.LogWarning($"[NametagMod] Icon download failed ({(isPc ? "PC" : "Standalone")}): {www.error}");
                }
            }
            catch (System.Exception ex)
            {
                NametagPlugin.Log.LogError($"[NametagMod] DownloadIconAsync: {ex.Message}");
            }
        }

        private static Sprite MakeSprite(Texture2D tex) =>
            Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), SPRITE_PPU);

        private static Sprite SpriteForPlatform(string platform)
        {
            if (platform == "PC" || platform == "Steam") return pcSprite;
            if (platform == "Standalone") return standaloneSprite;
            return null;
        }

        public static void OnUpdate()
        {
            if (!iconsLoaded) LoadIcons();

            var allRigs = VRRigCache.ActiveRigs;
            if (allRigs == null) return;

            float cfgSize = NametagPlugin.NametagSize.Value;
            float cfgHeight = NametagPlugin.NametagHeight.Value;
            string cfgColorHex = NametagPlugin.OutlineColor.Value;
            float cfgThickness = NametagPlugin.OutlineThickness.Value;
            float cfgQuality = NametagPlugin.OutlineQuality.Value;

            bool sizeChanged = !Mathf.Approximately(cfgSize, _lastSize);
            bool heightChanged = !Mathf.Approximately(cfgHeight, _lastHeight);
            bool outlineChanged = cfgColorHex != _lastColorHex
                               || !Mathf.Approximately(cfgThickness, _lastThickness)
                               || !Mathf.Approximately(cfgQuality, _lastQuality);

            if (sizeChanged || heightChanged || outlineChanged)
            {
                _lastSize = cfgSize;
                _lastHeight = cfgHeight;
                _lastColorHex = cfgColorHex;
                _lastThickness = cfgThickness;
                _lastQuality = cfgQuality;
            }

            Color cfgOutlineColor = ParseHexColor(cfgColorHex);

            remove.Clear();
            foreach (var kvp in tags)
            {
                if (kvp.Key == null || !allRigs.Contains(kvp.Key) || kvp.Key.isLocal)
                {
                    if (kvp.Value.root != null) Object.Destroy(kvp.Value.root);
                    if (kvp.Value.auraGO != null) Object.Destroy(kvp.Value.auraGO);
                    kvp.Value.auraPS = null;
                    remove.Add(kvp.Key);
                }
            }
            foreach (var r in remove) tags.Remove(r);

            for (int i = 0; i < allRigs.Count; i++)
            {
                var rig = allRigs[i];
                if (rig == null || rig.isLocal) continue;

                if (!tags.TryGetValue(rig, out TagData tag))
                    tag = CreateTag(rig);

                if (tag == null) continue;

                if (tag.root != null && sizeChanged)
                    tag.root.transform.localScale = Vector3.one * cfgSize;

                if (outlineChanged)
                    foreach (var lbl in tag.AllLabels())
                        ApplyOutline(lbl, cfgOutlineColor, cfgThickness, cfgQuality);

                UpdatePosition(rig, tag, cfgHeight);
                RefreshText(rig, tag);
            }
        }

        private static TagData CreateTag(VRRig rig)
        {
            try
            {
                string playerName = rig.OwningNetPlayer?.NickName ?? "Unknown";
                string userId = rig.OwningNetPlayer?.UserId ?? "N/A";
                Color playerColor = rig.playerColor;
                int fps = GetFPS(rig);
                string platform = GetPlatform(rig);
                Sprite icon = SpriteForPlatform(platform);

                float cfgSize = NametagPlugin.NametagSize.Value;
                float cfgHeight = NametagPlugin.NametagHeight.Value;
                Color cfgOutColor = ParseHexColor(NametagPlugin.OutlineColor.Value);
                float cfgThickness = NametagPlugin.OutlineThickness.Value;
                float cfgQuality = NametagPlugin.OutlineQuality.Value;

                var root = new GameObject($"Nametag_{playerName}");
                root.transform.localScale = Vector3.one * cfgSize;

                GetLabelOffsets(platform, out float nameY, out float idY, out float fpsY, out float creationY);

                SpriteRenderer sr = null;
                if (icon != null)
                {
                    var spriteGo = new GameObject("Sprite");
                    spriteGo.transform.SetParent(root.transform, false);
                    spriteGo.transform.localScale = Vector3.one * SPRITE_LOCAL_SCALE;
                    spriteGo.transform.localPosition = new Vector3(ICON_FPS_X, fpsY, -0.05f);
                    sr = spriteGo.AddComponent<SpriteRenderer>();
                    sr.sprite = icon;
                    sr.sortingOrder = 1000;
                }

                var nameText = MakeLabel(root, "Name", playerName, NAME_FONT_SIZE,
                    playerColor, bold: true, localY: nameY,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);

                var idText = MakeLabel(root, "Id", userId, ID_FONT_SIZE,
                    new Color(0.75f, 0.55f, 1f), bold: false, localY: idY,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);

                var pipeText = MakeLabel(root, "Pipe", "|", FPS_FONT_SIZE,
                    Color.white, bold: true, localY: fpsY, localX: PIPE_TEXT_X, maxWidth: 2f,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);

                var fpsText = MakeLabel(root, "FPS", FormatFPS(fps), FPS_FONT_SIZE,
                    FPSColor(fps), bold: true, localY: fpsY, localX: FPS_TEXT_X, maxWidth: 6f,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);

                var creationText = MakeLabel(root, "Creation", "...", CREATION_FONT_SIZE,
                    new Color(0.65f, 0.65f, 0.65f), bold: false, localY: creationY,
                    maxWidth: 18f, align: TextAlignmentOptions.Center,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);

                FetchCreationDate(userId);

                var modText = MakeLabel(root, "ModLabel", "", MOD_LABEL_FONT_SIZE,
                    MOD_LABEL_COLOR, bold: true, localY: nameY + MOD_LABEL_Y_ABOVE_NAME,
                    maxWidth: 32f, align: TextAlignmentOptions.Center,
                    outlineColor: cfgOutColor, outlineThickness: cfgThickness, outlineQuality: cfgQuality);
                modText.gameObject.SetActive(false);

                var tag = new TagData
                {
                    root = root,
                    spriteRenderer = sr,
                    nameText = nameText,
                    idText = idText,
                    pipeText = pipeText,
                    fpsText = fpsText,
                    creationText = creationText,
                    modText = modText,
                    cachedName = playerName,
                    cachedId = userId,
                    cachedPlatform = platform,
                    cachedFPS = fps,
                };

                tag.auraGO = CreatePlayerAura(rig, playerColor);
                if (tag.auraGO != null)
                    tag.auraPS = tag.auraGO.GetComponent<ParticleSystem>();
                tag.cachedAuraColor = playerColor;

                tags[rig] = tag;
                UpdatePosition(rig, tag, cfgHeight);
                return tag;
            }
            catch (System.Exception ex)
            {
                NametagPlugin.Log.LogError($"[NametagMod] CreateTag: {ex.Message}");
                return null;
            }
        }

        private static void RefreshText(VRRig rig, TagData tag)
        {
            string userId = rig.OwningNetPlayer?.UserId ?? "N/A";
            int fps = GetFPS(rig);
            string platform = GetPlatform(rig);

            Color gorillaTint = rig.playerColor;
            if (tag.auraPS != null && gorillaTint != tag.cachedAuraColor)
            {
                tag.cachedAuraColor = gorillaTint;
                RecolorAura(tag.auraPS, gorillaTint);
            }
            if (tag.auraGO == null)
            {
                tag.auraGO = CreatePlayerAura(rig, gorillaTint);
                if (tag.auraGO != null)
                {
                    tag.auraPS = tag.auraGO.GetComponent<ParticleSystem>();
                    tag.cachedAuraColor = gorillaTint;
                }
            }

            if (Time.time - tag.modLastCheck >= NametagPlugin.UpdateInterval.Value)
            {
                tag.modLastCheck = Time.time;
                string modLine = BuildModLine(rig);
                if (modLine != tag.cachedModLine)
                {
                    tag.cachedModLine = modLine;
                    if (tag.modText != null)
                    {
                        if (string.IsNullOrEmpty(modLine))
                            tag.modText.gameObject.SetActive(false);
                        else
                        {
                            tag.modText.text = modLine;
                            tag.modText.gameObject.SetActive(true);
                        }
                    }
                }
            }

            string name = rig.OwningNetPlayer?.NickName ?? "Unknown";
            Color playerColor = rig.playerColor;
            if (name != tag.cachedName && tag.nameText != null)
            {
                tag.nameText.text = name;
                tag.nameText.color = playerColor;
                tag.nameText.alignment = TextAlignmentOptions.Center;
                tag.cachedName = name;
            }
            else if (tag.nameText != null)
                tag.nameText.color = playerColor;

            if (userId != tag.cachedId && tag.idText != null)
            {
                tag.idText.text = userId;
                tag.idText.color = new Color(0.75f, 0.55f, 1f);
                tag.cachedId = userId;
            }

            if (fps != tag.cachedFPS && tag.fpsText != null)
            {
                tag.fpsText.text = FormatFPS(fps);
                tag.fpsText.color = FPSColor(fps);
                tag.cachedFPS = fps;
            }

            if (platform != tag.cachedPlatform)
            {
                tag.cachedPlatform = platform;
                GetLabelOffsets(platform, out float nameY, out float idY, out float fpsY, out float creationY);

                SetLocalY(tag.nameText?.transform, nameY);
                SetLocalY(tag.idText?.transform, idY);
                SetLocalY(tag.pipeText?.transform, fpsY);
                SetLocalY(tag.fpsText?.transform, fpsY);
                SetLocalY(tag.creationText?.transform, creationY);
                SetLocalY(tag.modText?.transform, nameY + MOD_LABEL_Y_ABOVE_NAME);

                if (tag.spriteRenderer != null)
                {
                    tag.spriteRenderer.sprite = SpriteForPlatform(platform);
                    var sp = tag.spriteRenderer.transform.localPosition;
                    tag.spriteRenderer.transform.localPosition = new Vector3(ICON_FPS_X, fpsY, sp.z);
                }
            }

            if (creationDateCache.TryGetValue(userId, out string creation) && creation != tag.cachedCreation)
            {
                tag.cachedCreation = creation;
                if (tag.creationText != null)
                {
                    tag.creationText.text = creation == "Null" ? "Unknown" : creation;
                    tag.creationText.alignment = TextAlignmentOptions.Center;
                }
            }
        }

        private static readonly Dictionary<string, string> FRIENDLY_NAMES =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "$PayLoad$",                                        "Katalyst" },
            { "GrateVersion",                                     "Grate Menu" },
            { "Grate",                                            "Grate Menu" },
            { "I like cheese",                                    "Cheese Menu" },
            { "cheese is gouda",                                  "Cheese Menu" },
            { "kingbingus.oculusreportmenu",                      "Kingbingus Report Menu" },
            { "CustomMaterial",                                   "Custom Materials" },
            { "Boy Do I Love Information",                        "BDILI" },
            { "BDILI",                                            "BDILI" },
            { "BoyDoILoveInformation",                            "BDILI" },
            { "BoyDoILoveInfo",                                   "BDILI" },
            { "BoyDoILoveInformation Public: True",               "BDILI Public" },
            { "BoyDoILoveInformation Public: False",              "BDILI Private" },
            { "BoyDoILoveInformation Public",                     "BDILI" },
            { "DTAOI",                                            "DTAOI" },
            { "Emote Wheel",                                      "Emote Wheel" },
            { "EmoteWheel",                                       "Emote Wheel" },
            { "Vortex Emotes",                                    "Vortex Emotes" },
            { "Colossal Emotes",                                  "Colossal Emotes" },
            { "Thragg Client",                                    "Thragg Client" },
            { "WhoDis",                                           "Who Dis" },
            { "GorillaNametags",                                  "Gorilla Nametags" },
            { "NT",                                               "Nametags" },
            { "FPSNametags",                                      "FPS Nametags" },
            { "NametagsPlusPlus",                                  "Nametags++" },
            { "GoldensNametags",                                  "Goldens Nametags" },
            { "CSVersion",                                        "CS Version" },
            { "ShirtProperties",                                  "Shirt Properties" },
            { "shirtversion",                                     "Shirt Version" },
            { "MonkePhone",                                       "Monke Phone" },
            { "msp",                                              "Monke Smartphone" },
            { "Graze Heath System",                               "Graze Health" },
            { "github.com/ZlothY29IQ/GorillaMediaDisplay",       "Gorilla Media Display" },
            { "Gorilla Track Packed",                             "Gorilla Track Packed" },
            { "Gorilla Track",                                    "Gorilla Track" },
            { "Gorilla Track 2.3.0",                             "Gorilla Track" },
            { "CarName",                                          "Car Name" },
            { "github.com/ZlothY29IQ/MonkeClick",                "Monke Click" },
            { "github.com/ZlothY29IQ/MonkeClick-CI",             "Monke Click CI" },
            { "github.com/ZlothY29IQ/TooMuchInfo",               "Too Much Info" },
            { "github.com/ZlothY29IQ/MonkeRealism",              "Monke Realism" },
            { "github.com/ZlothY29IQ/RoomUtils-IW",              "Room Utils IW" },
            { "github.com/ZlothY29IQ/Zloth-RecRoomRig",          "Zloth RecRoom Rig" },
            { "github.com/maroon-shadow/SimpleBoards",           "Simple Boards" },
            { "github.com/arielthemonke/GorillaCraftAutoBuilder", "Gorilla Craft" },
            { "GC",                                               "GC" },
            { "GS",                                               "GS" },
            { "GorillaWatch",                                     "Gorilla Watch" },
            { "GorillaShirts",                                    "Gorilla Shirts" },
            { "GorillaShirtsFeb26",                               "Gorilla Shirts" },
            { "GorillaCosmetics",                                 "Gorilla Cosmetics" },
            { "GorillaCinema",                                    "Gorilla Cinema" },
            { "GorillaTorsoEstimator",                            "Gorilla Torso Estimator" },
            { "gorillastats",                                     "Gorilla Stats" },
            { "HP_Left",                                          "HP Left" },
            { "DeeTags",                                          "Dee Tags" },
            { "InfoWatch",                                        "Info Watch" },
            { "GTrials",                                          "G Trials" },
            { "ChainedTogetherActive",                            "Chained Together" },
            { "chainedtogether",                                  "Chained Together" },
            { "GPronouns",                                        "G Pronouns" },
            { "BananaOS",                                         "Banana OS" },
            { "BananaPhone",                                      "Banana Phone" },
            { "FPS-Nametags for Zlothy",                          "FPS Nametags" },
            { "WalkSimulator",                                    "Walk Simulator" },
            { "MediaPad",                                         "Media Pad" },
            { "Dingus",                                           "Dingus" },
            { "Body Tracking",                                    "Body Tracking" },
            { "Body Estimation",                                  "Body Estimation" },
            { "WhoIsThatMonke",                                   "Who Is That Monke" },
            { "WhoIsThatMonke Version",                           "Who Is That Monke" },
            { "GFaces",                                           "G Faces" },
            { "ObsidianMC",                                       "Obsidian MC" },
            { "genesis",                                          "Genesis" },
            { "elux",                                             "Elixir" },
            { "VioletFreeUser",                                   "Violet Free" },
            { "violetfree",                                       "Violet Free" },
            { "VioletPaidUser",                                   "Violet Paid" },
            { "Violet On Top",                                    "Violet" },
            { "Hidden Menu",                                      "Hidden Menu" },
            { "void",                                             "Void" },
            { "void_menu_open",                                   "Void Menu Open" },
            { "6XpyykmrCthKhFeUfkYGxv7xnXpoe2",                  "Unknown Mod" },
            { "6p72ly3j85pau2g9mda6ib8px",                       "Unknown Mod" },
            { "y u lookin in here weirdo",                        "Unknown Mod" },
            { "hgrehngio889584739_hugb",                          "Unknown Mod" },
            { "cronos",                                           "Cronos" },
            { "ORBIT",                                            "Orbit" },
            { "ØƦƁƖƬ",                                           "Orbit" },
            { "ElixirMenu",                                       "Elixir Menu" },
            { "Elixir",                                           "Elixir" },
            { "Fusioned",                                         "Fusioned" },
            { "MistUser",                                         "Mist User" },
            { "Untitled",                                         "Untitled" },
            { "dark",                                             "Dark" },
            { "oblivionuser",                                     "Oblivion User" },
            { "eyerock reborn",                                   "EyeRock Reborn" },
            { "asteroidlite",                                     "Asteroid Lite" },
            { "cokecosmetics",                                    "Coke Cosmetics" },
            { "FNgMenu",                                          "FNg Menu" },
            { "Atlas",                                            "Atlas" },
            { "MP25",                                             "MP25" },
            { "Lozar",                                            "Lozar" },
            { "LozarosFree",                                      "Lozaros Free" },
            { "pmversion",                                        "PM Version" },
            { "monkehavocversion",                                "Monke Havoc Version" },
            { "silliness",                                        "Silliness" },
            { "IIsStupidMenu",                                    "ii's Stupid Menu" },
            { "iiStupidMenu",                                     "ii's Stupid Menu" },
            { "iimenu",                                           "ii Menu" },
            { "Wyvern",                                           "Wyvern" },
            { "Spectral",                                         "Spectral" },
            { "drowsiiiGorillaInfoBoard",                         "Drowsiii Info Board" },
            { "Vivid",                                            "Vivid" },
            { "MonkeCosmetics::Material",                         "Monke Cosmetics Material" },
            { "usinggphys",                                       "Using GPhys" },
            { "GPhysVersion",                                     "GPhys" },
            { "tictactoe",                                        "Tic Tac Toe" },
            { "TicTacToeBoard",                                   "Tic Tac Toe Board" },
            { "ccolor",                                           "CColor" },
            { "goofywalkversion",                                 "Goofy Walk" },
            { "platform",                                         "Platform Tag" },
            { "BepInEx",                                          "BepInEx" },
            { "CosmetX",                                          "CosmetX" },
            { "CosmeticsVersion",                                 "Custom Cosmetics" },
            { "BarkVersion",                                      "Bark Menu" },
            { "Utilla",                                           "Utilla" },
            { "PxslWare",                                         "Pxslware Client" },
            { "PlayerModel",                                      "Custom Player Models" },
            { "ForeverCosmetx",                                   "Forever Cosmetx" },
            { "MonkeModManager",                                  "Monke Mod Manager" },
            { "YizziCam",                                         "Yizzi Camera" },
            { "ShibaGT",                                          "ShibaGT Menu" },
            { "NXO",                                              "NXO Menu" },
            { "ZoloTroll",                                        "Zolo Troll Menu" },
            { "ColossalMenu",                                     "Colossal Menu" },
            { "MalachiMenu",                                      "Malachi Menu" },
            { "Resurgence",                                       "Resurgence Menu" },
            { "Resurge",                                          "Resurge Menu" },
            { "HANBody",                                          "HAN Body Estimation" },
            { "IndexColor",                                       "Index Mods" },
            { "morphine",                                         "Morphine Menu" },
            { "SMC",                                              "Mod Checker" },
            { "KameColor",                                        "Kame Color" },
            { "ProfilePictures",                                  "Profile Pics" },
            { "CurrentSong",                                      "Current Song" },
            { "EIOP",                                             "EIOP" },
        };

        private static string FriendlyKey(string k)
        {
            if (FRIENDLY_NAMES.TryGetValue(k, out string friendly))
                return friendly;

            var sb = new System.Text.StringBuilder(k.Length + 4);
            for (int i = 0; i < k.Length; i++)
            {
                char c = k[i];
                if (c == '_' || c == '-') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && (char.IsLower(k[i - 1]) || char.IsDigit(k[i - 1])))
                    sb.Append(' ');
                sb.Append(c);
            }
            string result = sb.ToString().Trim();
            return result.Length > 0 ? char.ToUpper(result[0]) + result.Substring(1) : result;
        }

        private static string BuildModLine(VRRig rig)
        {
            try
            {
                string userId = rig.OwningNetPlayer?.UserId;
                if (string.IsNullOrEmpty(userId)) return null;

                Photon.Realtime.Player photonPlayer = null;
                foreach (var p in PhotonNetwork.PlayerList)
                    if (p.UserId == userId) { photonPlayer = p; break; }

                if (photonPlayer == null) return null;

                var props = photonPlayer.CustomProperties;
                if (props == null || props.Count == 0) return null;

                var seen = new HashSet<string>();
                var names = new List<string>();
                foreach (var key in props.Keys)
                {
                    string k = key as string;
                    if (string.IsNullOrEmpty(k) || NATIVE_GT_KEYS.Contains(k)) continue;
                    string name = FriendlyKey(k);
                    if (seen.Add(name)) names.Add(name);
                }

                if (names.Count == 0) return null;

                var line = new System.Text.StringBuilder("Mods: ");
                for (int i = 0; i < names.Count; i++)
                {
                    string part = (i == 0 ? "" : ", ") + names[i];
                    if (line.Length + part.Length > MOD_LINE_MAX_CHARS) { line.Append("…"); break; }
                    line.Append(part);
                }
                return line.ToString();
            }
            catch { return null; }
        }

        private static void UpdatePosition(VRRig rig, TagData tag, float cfgHeight)
        {
            if (rig?.headMesh?.transform == null || tag?.root == null) return;

            Vector3 headPos = rig.headMesh.transform.position;
            tag.root.transform.position = headPos + Vector3.up * cfgHeight;

            if (Camera.main != null)
            {
                tag.root.transform.rotation = Quaternion.LookRotation(
                    tag.root.transform.position - Camera.main.transform.position);
            }

            if (tag.auraGO != null)
                tag.auraGO.transform.position = headPos;
        }

        private static int GetFPS(VRRig rig)
        {
            if (rig == null || fpsField == null) return 0;
            try { return fpsField.GetValue(rig) is int v ? v : 0; }
            catch { return 0; }
        }

        private static string GetPlatform(VRRig rig)
        {
            if (rig == null) return "Unknown";
            try
            {
                if (cosmeticsAllowedField != null)
                {
                    string s = cosmeticsAllowedField.GetValue(rig) as string;
                    if (!string.IsNullOrEmpty(s))
                    {
                        if (s.Contains("S. FIRST LOGIN")) return "Steam";
                        if (s.Contains("FIRST LOGIN")) return "PC";
                    }
                }
                if (creatorField != null)
                {
                    var creator = creatorField.GetValue(rig);
                    if (creator != null)
                    {
                        var getRef = creator.GetType().GetMethod("GetPlayerRef");
                        if (getRef != null)
                        {
                            var playerRef = getRef.Invoke(creator, null) as Photon.Realtime.Player;
                            if (playerRef?.CustomProperties?.Count >= 2)
                                return "PC";
                        }
                    }
                }
            }
            catch { }
            return "Standalone";
        }

        private static void FetchCreationDate(string userId)
        {
            if (string.IsNullOrEmpty(userId) || userId == "N/A") return;
            if (creationDateCache.ContainsKey(userId)) return;
            creationDateCache[userId] = "...";
            PlayFabClientAPI.GetAccountInfo(
                new GetAccountInfoRequest { PlayFabId = userId },
                result => { creationDateCache[userId] = result.AccountInfo.Created.ToString("MM/dd/yyyy"); },
                error => { creationDateCache[userId] = "Null"; });
        }

        private static void GetLabelOffsets(string platform, out float nameY, out float idY,
                                             out float fpsY, out float creationY)
        {
            if (platform == "PC" || platform == "Steam")
            { nameY = STEAM_NAME_Y; idY = STEAM_ID_Y; fpsY = STEAM_FPS_Y; creationY = STEAM_CREATION_Y; }
            else
            { nameY = META_NAME_Y; idY = META_ID_Y; fpsY = META_FPS_Y; creationY = META_CREATION_Y; }
        }

        private static void SetLocalY(Transform t, float y)
        {
            if (t == null) return;
            var p = t.localPosition;
            t.localPosition = new Vector3(p.x, y, p.z);
        }

        private static string FormatFPS(int fps) => fps <= 0 ? "FPS: --" : $"FPS: {fps}";

        private static Color FPSColor(int fps)
        {
            if (fps >= 145) return new Color(0.25f, 0.60f, 1.00f);
            if (fps >= 75) return new Color(0.20f, 0.85f, 0.20f);
            if (fps >= 46) return new Color(1.00f, 0.80f, 0.10f);
            return new Color(0.90f, 0.20f, 0.20f);
        }

        private static Color ParseHexColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c))
                return c;
            NametagPlugin.Log.LogWarning($"[NametagMod] Invalid OutlineColor '{hex}', using default.");
            return new Color(0f, 0f, 0f, 0.90f);
        }

        private static void ApplyOutline(TextMeshPro tmp, Color color, float thickness, float softness)
        {
            if (tmp == null) return;
            tmp.outlineColor = new Color32(
                (byte)(color.r * 255f), (byte)(color.g * 255f),
                (byte)(color.b * 255f), (byte)(color.a * 255f));
            tmp.outlineWidth = thickness;
            if (tmp.fontMaterial != null)
                tmp.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineSoftness, softness);
        }

        private static TextMeshPro MakeLabel(
            GameObject parent, string objName, string text, float fontSize,
            Color color, bool bold, float localY, float localX = 0f, float maxWidth = 18f,
            TextAlignmentOptions align = TextAlignmentOptions.Center,
            Color outlineColor = default, float outlineThickness = 0.2f, float outlineQuality = 0.0f)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(localX, localY, -0.05f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(maxWidth, 2.5f);
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.sortingOrder = 1001;

            if (sharedFont != null) tmp.font = sharedFont;
            ApplyOutline(tmp, outlineColor, outlineThickness, outlineQuality);
            return tmp;
        }

        private static GameObject CreatePlayerAura(VRRig rig, Color playerColor)
        {
            if (rig == null) return null;

            var go = new GameObject("PlayerAura");
            go.transform.SetParent(rig.transform, false);
            go.transform.localScale = Vector3.one;

            if (rig.headMesh != null) go.transform.position = rig.headMesh.transform.position;
            else go.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(playerColor.r, playerColor.g, playerColor.b, 1.0f),
                new Color(playerColor.r * 0.8f, playerColor.g * 0.8f, playerColor.b * 0.8f, 0.9f));
            main.maxParticles = 35;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.rateOverTime = 14f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.20f;
            shape.radiusThickness = 0.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(BuildAuraGradient(playerColor));

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 1.0f),
                    new Keyframe(0.6f, 0.7f),
                    new Keyframe(1f, 0.15f)));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                rend.renderMode = ParticleSystemRenderMode.Billboard;
                rend.sortingOrder = 997;
                Shader sh = Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Particles/Additive")
                         ?? Shader.Find("Legacy Shaders/Particles/Additive")
                         ?? Shader.Find("Sprites/Default");
                if (sh != null)
                    rend.material = new Material(sh) { color = new Color(playerColor.r, playerColor.g, playerColor.b, 1f) };
            }

            ps.Play();
            return go;
        }

        private static Gradient BuildAuraGradient(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(c.r,         c.g,         c.b),         0.0f),
                    new GradientColorKey(new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.85f), 0.5f),
                    new GradientColorKey(new Color(c.r * 0.5f,  c.g * 0.5f,  c.b * 0.5f),  1.0f),
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1.00f, 0.0f),
                    new GradientAlphaKey(0.50f, 0.5f),
                    new GradientAlphaKey(0.00f, 1.0f),
                });
            return g;
        }

        private static void RecolorAura(ParticleSystem ps, Color c)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(c.r, c.g, c.b, 1.0f),
                new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 0.9f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(BuildAuraGradient(c));
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend?.material != null)
                rend.material.color = new Color(c.r, c.g, c.b, 1f);
        }

        public static void Cleanup()
        {
            foreach (var kvp in tags)
            {
                if (kvp.Value.root != null) Object.Destroy(kvp.Value.root);
                if (kvp.Value.auraGO != null) Object.Destroy(kvp.Value.auraGO);
            }
            tags.Clear();
        }
    }
}