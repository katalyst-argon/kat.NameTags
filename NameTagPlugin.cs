using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace NametagMod
{
    [BepInPlugin("com.katalyst.nametaginit", "NametagMod", "1.0.0")]
    [BepInProcess("Gorilla Tag.exe")]
    public class NametagPlugin : BaseUnityPlugin
    {
        public const string MOD_PROP_KEY = "$PayLoad$";
        public const string MOD_PROP_VALUE = "True";

        public static NametagPlugin Instance { get; private set; }
        public static new ManualLogSource Log { get; private set; }

        public static ConfigEntry<float> NametagSize;
        public static ConfigEntry<float> NametagHeight;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<string> OutlineColor;
        public static ConfigEntry<float> OutlineThickness;
        public static ConfigEntry<float> OutlineQuality;

        private Harmony harmony;
        private bool initialised = false;
        private bool propBroadcast = false;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;

            NametagSize = Config.Bind(
                "Layout", "NametagSize", 0.13f,
                new ConfigDescription("Uniform scale of the entire nametag.",
                    new AcceptableValueRange<float>(0.04f, 0.50f)));

            NametagHeight = Config.Bind(
                "Layout", "NametagHeight", 0.23f,
                new ConfigDescription("Vertical offset above the player's head in world units.",
                    new AcceptableValueRange<float>(0.00f, 1.50f)));

            UpdateInterval = Config.Bind(
                "Layout", "UpdateInterval", 5.0f,
                new ConfigDescription("How often (seconds) mod-detection rescans Photon properties.",
                    new AcceptableValueRange<float>(0.5f, 60.0f)));

            OutlineColor = Config.Bind(
                "Outline", "OutlineColor", "#000000E6",
                "Hex colour of the text outline. Accepts #RRGGBB or #RRGGBBAA.");

            OutlineThickness = Config.Bind(
                "Outline", "OutlineThickness", 0.2f,
                new ConfigDescription("Outline thickness (0 = none, 1 = maximum).",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            OutlineQuality = Config.Bind(
                "Outline", "OutlineQuality", 0.0f,
                new ConfigDescription("Outline edge softness (0 = sharp, 1 = blurry).",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            harmony = new Harmony("com.katalyst.nametag");
            harmony.PatchAll();

            Log.LogInfo("KatalystNameTags loaded.");
        }

        private void Update()
        {
            if (!initialised && VRRigCache.ActiveRigs != null)
            {
                NametagDisplay.Init();
                initialised = true;
                Log.LogInfo("KatalystNameTags initialised.");
            }

            if (!propBroadcast && PhotonNetwork.InRoom)
                BroadcastModProperty();

            if (initialised)
                NametagDisplay.OnUpdate();
        }

        private void BroadcastModProperty()
        {
            try
            {
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { MOD_PROP_KEY, MOD_PROP_VALUE } });
                propBroadcast = true;
                Log.LogInfo("[KatalystNameTags] Mod property broadcast to room.");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[KatalystNameTags] Failed to broadcast mod property: {ex.Message}");
            }
        }

        private void OnJoinedRoom() => propBroadcast = false;

        private void OnDestroy()
        {
            NametagDisplay.Cleanup();
            harmony?.UnpatchSelf();
        }
    }
}