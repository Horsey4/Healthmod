using MSCLoader;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using HutongGames.PlayMaker;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace HealthMod
{
    public class Health : Mod
    {
        public override string ID => "Health";
        public override string Name => "Health";
        public override string Author => "Horsey4";
        public override string Version => "1.2.3";
        public override bool SecondPass => true;
        public static int apiVer => 5;
        internal static string saveFile = $@"{Application.persistentDataPath}\Health.dat";
        internal static FsmFloat _drunk = FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
        internal static FsmString _vehicle = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
        internal static FsmFloat _fatigue = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
        internal static FsmFloat _burns = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBurns");
        internal static FsmBool _sleeping = FsmVariables.GlobalVariables.FindFsmBool("PlayerSleeps");
        internal static List<Action> routines = new List<Action>();
        internal static Settings vanillaMode = new Settings("vanillaMode", "Vanilla Mode", false);
        internal static Settings crashHpLoss = new Settings("crashHpLoss", "Crash Damage", true, updateSettings);
        internal static Settings difficulty = new Settings("difficulty", "Difficulty Multiplier (x)", 1f, updateSettings);
        internal static Settings minCrashSpeed = new Settings("minCrashSpeed", "Minimum Crash Speed (km/h)", 20, updateSettings);
        internal static List<FsmFloat> deathSpeeds = new List<FsmFloat>();
        internal static FsmFloat[] _stats =
        {
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerThirst"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerHunger"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerUrine")
        };
        internal static Material hudMat;
        internal static Transform HUD;
        internal static Transform hpBar;
        internal static BlurOptimized damageEffect;
        internal static PlayMakerFSM wiringFsm;
        internal static PlayMakerFSM callFsm;
        internal static PlayMakerFSM swimFsm;
        internal static FsmVariables deathVars;
        internal static float difficultyMulti;
        internal static float pHunger;
        internal static float crashMulti;
        internal static float crashCooldown;
        internal static float crashMin;
        internal static float oldForce;
        internal static int sleepCounter;
        internal static bool _mode;

        public static GameObject death { get; internal set; }
        public static Transform player { get; internal set; }
        public static FsmFloat wasp { get; internal set; }
        public static ConfigurableJoint vehiJoint { get; internal set; }
        public static bool isLoaded => ModLoader.IsModPresent("Health");
        public static FsmFloat[] stats => _stats;
        public static FsmFloat drunk => _drunk;
        public static FsmString vehicle => _vehicle;
        public static FsmFloat fatigue => _fatigue;
        public static FsmFloat burns => _burns;
        public static FsmBool sleeping => _sleeping;
        public static bool crashDamage => (bool)crashHpLoss.Value;
        public static bool mode => _mode;
        public static AudioClip[] hitSfx = new AudioClip[8];
        public static float hp;
        public static int poisonCounter;

        public override void OnNewGame() => File.Delete(saveFile);

        public override void OnSave()
        {
            var bytes = new byte[8];
            BitConverter.GetBytes(hp).CopyTo(bytes, 0);
            BitConverter.GetBytes(poisonCounter).CopyTo(bytes, 4);
            File.WriteAllBytes(saveFile, bytes);
        }

        public override void ModSettings()
        {
            Settings.AddText(this, "Vanilla mode cannot be toggled mid-game");
            Settings.AddCheckBox(this, vanillaMode);
            Settings.AddCheckBox(this, crashHpLoss);
            Settings.AddSlider(this, difficulty, 0.5f, 3f);
            Settings.AddSlider(this, minCrashSpeed, 10, 30);
        }

        public override void OnLoad()
        {
            // All mode variables
            player = GameObject.Find("PLAYER").transform;
            HUD = GameObject.Find("GUI/HUD").transform;
            var hpObj = GameObject.Instantiate(HUD.Find("Hunger"));
            var hpLabel = hpObj.Find("HUDLabel");
            var camera = player.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            var waspFsm = camera.GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Blindness");
            
            // All mode setup
            hpObj.name = "Health";
            hpObj.parent = HUD;
            hpLabel.GetComponent<TextMesh>().text = "Health";
            hpLabel.Find("HUDLabelShadow").GetComponent<TextMesh>().text = "Health";
            hpObj.localPosition = ModLoader.IsModPresent("dirtmenag")
                ? new Vector3(-11.5f, 7.2f) : new Vector3(-11.5f, 6.8f);
            GameObject.Destroy(hpObj.GetComponentInChildren<PlayMakerFSM>());

            // All mode variables
            death = GameObject.Find("Systems").transform.Find("Death").gameObject;
            hpBar = GameObject.Find("GUI/HUD/Health/Pivot").transform;
            hudMat = hpBar.Find("HUDBar").GetComponent<MeshRenderer>().material;
            wasp = waspFsm.FsmVariables.FindFsmFloat("MaxAllergy");
            _mode = (bool)vanillaMode.Value;

            if (mode) routines.Add(() =>
            {
                setHp(100 - Mathf.Max(stats[0].Value - 100, stats[1].Value - 100, stats[2].Value - 100, stats[3].Value - 100, burns.Value, wasp.Value) - hp, null);
            });
            else
            {
                // Non vanilla mode variables
                pHunger = stats[1].Value;
                damageEffect = camera.gameObject.AddComponent<BlurOptimized>();
                deathVars = death.GetComponent<PlayMakerFSM>().FsmVariables;

                // FSM setup
                FsmVariables.GlobalVariables.FindFsmBool("OptionsPermaDeath").Value = false;
                var audio = GameObject.Find("MasterAudio/PlayerMisc").transform;
                try
                {
                    var actions = waspFsm.FsmStates.FirstOrDefault(x => x.Name == "Allergy").Actions;
                    actions[1].Enabled = false; // Disable vignetting for wasp stings
                    actions[2].Enabled = false;
                    actions[3].Enabled = false;

                    routines.Add(() =>
                    {
                        if (wasp.Value > 0)
                        {
                            if (damage(wasp.Value * 20, 0.05f, "Wasp")) kill("Wasp");
                            wasp.Value = 0;
                        }
                    });
                }
                catch (Exception e) { log($"Failed to hook waspFsm\n{e}"); } // waspFsm
                try
                {
                    wiringFsm = GameObject.Find("SATSUMA(557kg, 248)/Wiring").GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Shock");
                    wiringFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;

                    routines.Add(() =>
                    {
                        if (wiringFsm.ActiveStateName == "Random")
                        {
                            wiringFsm.SendEvent("OFF");
                            if (damage(50, 0.5f, "Electrocute")) kill("Electrocute");
                        }
                    });
                }
                catch (Exception e) { log($"Failed to hook wiringFsm\n{e}"); } // wiringFsm
                try
                {
                    callFsm = GameObject.Find("YARD/Building/LIVINGROOM").transform.Find("Telephone/Logic/Ring").GetComponent<PlayMakerFSM>();
                    callFsm.gameObject.SetActive(true);
                    if (!callFsm.transform.parent.gameObject.activeSelf)
                    {
                        callFsm.transform.parent.gameObject.SetActive(true);
                        callFsm.transform.parent.gameObject.SetActive(false);
                    }
                    callFsm.gameObject.SetActive(false);
                    callFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;

                    routines.Add(() =>
                    {
                        if (callFsm.ActiveStateName == "Random")
                        {
                            callFsm.SendEvent("SURVIVE");
                            if (damage(50, 0.5f, "PhoneThunder")) kill("PhoneThunder");
                        }
                    });
                }
                catch (Exception e) { log($"Failed to hook callFsm\n{e}"); } // callFsm
                try
                {
                    swimFsm = player.GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Swim");
                    var state = swimFsm.FsmStates.FirstOrDefault(x => x.Name == "Randomize");
                    state.Transitions[1].FsmEvent = swimFsm.FsmEvents.FirstOrDefault(x => x.Name == "SWIM");
                    state.Actions[0].Enabled = false;

                    routines.Add(() =>
                    {
                        if (swimFsm.ActiveStateName == "Randomize")
                        {
                            swimFsm.SendEvent("SWIM");
                            if (editHp(-5, "DrunkDrown")) kill("DrunkDrown");
                        }
                    });
                }
                catch (Exception e) { log($"Failed to hook swimFsm\n{e}"); } // swimFsm

                // Component Setup
                camera.Find("Drink/Hand/SpiritBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 100;
                camera.Find("Drink/Hand/BoozeBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 50;
                camera.Find("Drink/Hand/ShotGlass").gameObject.AddComponent<DrinkListener>().drinkMulti = 30;
                camera.Find("Drink/Hand/BeerBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 10;
                camera.Find("DeathBee").gameObject.AddComponent<BeeListener>();
                GameObject.Find("MAP").transform.Find("CloudSystem/Clouds/Thunder/GroundStrike").gameObject.AddComponent<LightningListener>();

                // Damage setup
                for (var i = 13; i < 21; i++)
                    hitSfx[i - 13] = audio.GetChild(i).GetComponent<AudioSource>().clip; // Get damage sfx
                damageEffect.blurShader = Shader.Find("Hidden/FastBlur");
                damageEffect.blurIterations = 2;
                damageEffect.downsample = 0;
                damageEffect.blurSize = 0;

                routines.Add(() =>
                {
                    if (pHunger != stats[1].Value)
                    {
                        var diff = pHunger - stats[1].Value;
                        if (diff > 0) editHp(diff, "Eat");
                        pHunger = stats[1].Value;
                    }
                    for (var i = 0; i < stats.Length; i++)
                        if (stats[i].Value > 100.1f)
                        {
                            if (stats[i].Name == "PlayerHunger") pHunger = 100.1f;
                            if (editHp((100.1f - stats[i].Value) / 1.5f, "StatDamage"))
                            {
                                kill(stats[i].Name.Substring(6));
                                stats[i].Value = 0;
                            }
                            else stats[i].Value = 100.1f;
                        }
                    if (burns.Value > 0)
                    {
                        if (editHp(-burns.Value, "Burn")) kill("Burn");
                        burns.Value = 0;
                    }
                });
            }
            try
            {
                var bytes = File.ReadAllBytes(saveFile);
                setHp(BitConverter.ToSingle(bytes, 0), "Load");
                poisonCounter = BitConverter.ToInt32(bytes, 4);
            }
            catch (Exception e) { setHp(100, $"LoadFallback\n{e}"); } // Load the save file
        }

        public override void SecondPassOnLoad()
        {
            log("Loading Stage 3");

            // Fix HUD with other mods
            var offset = 0f;
            var dirtinessDisable = ModLoader.IsModPresent("dirtmenag");
            if (ModLoader.IsModPresent("Alcohol_Meter"))
            {
                HUD.Find("Alcohol").localPosition = dirtinessDisable ? new Vector3(-11.5f, 6.8f) : new Vector3(-11.5f, 6.4f);
                offset += 0.4f;
            }
            if (ModLoader.IsModPresent("Playtime_and_clock_in_HUD"))
            {
                if (HUD.Find("Playtime").gameObject.activeSelf) offset += 0.4f;
                if (HUD.Find("Clock").gameObject.activeSelf) offset += 0.4f;
            }
            if (dirtinessDisable) offset -= 0.4f;
            HUD.Find("Money").localPosition = new Vector3(-11.5f, 6.4f - offset);
            HUD.Find("Jailtime").localPosition = new Vector3(-11.5f, 6 - offset);

            // Other setup
            if (!mode)
            {
                var cars = Resources.FindObjectsOfTypeAll<CarDynamics>();
                for (var i = 0; i < cars.Length; i++)
                {
                    log($"Hooking {cars[i].name}, Root = {cars[i].transform.parent == null}");

                    if (cars[i].transform.parent)
                    {
                        if (cars[i].name == "TRUCK")
                        {
                            var transform = cars[i].transform.Find("Colliders/Cabin");
                            transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y - 0.3f, transform.localPosition.z);
                            transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, transform.localScale.z + 0.6f);
                        }
                        else if (cars[i].name == "POLSA")
                        {
                            var col = new GameObject("DeathColl2").transform;
                            col.SetParent(cars[i].transform);
                            col.gameObject.layer = 22;
                            col.gameObject.AddComponent<BoxCollider>();
                            col.localPosition = new Vector3(0, -0.2f, 1.9f);
                            col.localScale = new Vector3(1.4f, 0.5f, 0.5f);
                            col.localEulerAngles = Vector3.zero;
                        }
                        else if (cars[i].name.Contains("RALLYCAR"))
                        {
                            var col = new GameObject("DeathColl2").transform;
                            col.SetParent(cars[i].transform);
                            col.gameObject.layer = 22;
                            col.gameObject.AddComponent<BoxCollider>();
                            col.localPosition = new Vector3(0, -0.1f, 1.5f);
                            col.localScale = new Vector3(1.4f, 1, 0.5f);
                            col.localEulerAngles = Vector3.zero;
                        }
                    } // Fix the AI collider jank

                    cars[i].gameObject.AddComponent<CrashListener>();
                    var fsms = cars[i].GetComponents<PlayMakerFSM>();
                    if (cars[i].transform.parent != null && fsms.Length > 0)
                    {
                        for (var x = 0; x < fsms.Length; x++)
                            if (fsms[x].FsmName.Contains("Throttle")) deathSpeeds.Add(fsms[x].FsmVariables.FindFsmFloat("DeathSpeedMPS"));
                    }
                    else
                    {
                        var trigger = cars[i].GetComponentsInChildren<Collider>().FirstOrDefault(x => x.isTrigger && x.name == "DriveTrigger");
                        if (trigger) trigger.gameObject.AddComponent<SeatBeltListener>();
                    }
                }
            }
            updateSettings();
        }

        public override void FixedUpdate()
        {
            if (death.activeInHierarchy || mode) return;
            if (crashDamage)
            {
                if (vehicle.Value != "" && (!vehiJoint || vehiJoint && vehiJoint.breakForce != Mathf.Infinity))
                {
                    vehiJoint = player.GetComponentInParent<ConfigurableJoint>();
                    oldForce = vehiJoint.breakForce;
                    vehiJoint.breakForce = Mathf.Infinity;
                    vehiJoint.breakTorque = Mathf.Infinity;
                    switch (vehicle.Value)
                    {
                        case "Jonnez":
                        case "Boat":
                        case "Kekmet":
                            crashMulti = 4000 / oldForce;
                            break;
                        default:
                            crashMulti = 1000 / oldForce;
                            break;
                    }
                }
                if (crashCooldown > 0) crashCooldown -= 0.05f;
            }
            if (poisonCounter > 0)
            {
                poisonCounter--;
                if (editHp(-0.001f))
                    killCustom("Man killed\nfrom alcohol\npoisoning", "Mies kuoli\nalkoholimyrkytykseen");
            }
            if (sleeping.Value)
            {
                sleepCounter++;
                if (sleepCounter == 200) editHp(fatigue.Value, "Sleep");
            }
            else sleepCounter = 0;
        }

        public override void Update()
        {
            // De-blur
            if (!mode)
            {
                if (damageEffect.blurSize > 0)
                    damageEffect.blurSize -= Time.deltaTime * 4;
                else damageEffect.enabled = false;
            }

            if (death.activeInHierarchy)
            {
                hp = 100;
                poisonCounter = 0;
                return;
            }
            for (var i = 0; i < routines.Count; i++) routines[i]();
        }

        internal static void updateSettings()
        {
            difficultyMulti = Mathf.Clamp(Convert.ToSingle(difficulty.Value), 0.5f, 3);
            crashMin = Mathf.Clamp(Convert.ToSingle(minCrashSpeed.Value), 10, 30) * 5 / 18;

            log($"Settings updated:\n\tcrashHpLoss = {crashDamage}\n\tdifficultyMulti = {difficultyMulti}\n\tcrashMin = {crashMin}");

            if (deathSpeeds != null && !mode) for (var i = 0; i < deathSpeeds.Count; i++)
                deathSpeeds[i].Value = (bool)crashHpLoss.Value ? Mathf.Infinity : 5;
        }

        internal static void log(object obj) => Console.WriteLine($"[Health] {obj}");

        internal static void setHp(float val, string reason = null)
        {
            if (death.activeInHierarchy) return;
            hp = Mathf.Clamp(val, 0, 100);
            hpBar.localScale = new Vector3(hp / 100, 1);
            if (reason != null) log($"HP set to {hp} because {reason}");

            if (poisonCounter > 0) hudMat.color = Color.green;
            else hudMat.color = hp > 30 ? Color.white : Color.red;
        }

        public static bool editHp(float val, string reason = null)
        {
            setHp(hp + (val > 0 ? val / difficultyMulti : val * difficultyMulti), reason);
            return hp == 0;
        }

        public static bool damage(float val, float damageMulti = 1, string reason = null)
        {
            damageEffect.enabled = true;
            damageEffect.blurSize += val / 10;
            AudioSource.PlayClipAtPoint(hitSfx[UnityEngine.Random.Range(0, hitSfx.Length - 1)], player.transform.position);

            if (reason != null) log($"Blurred because {reason}");
            if (damageMulti == 0) return false;
            return editHp(-val * damageMulti, reason);
        }

        public static void kill(string type = null)
        {
            death.SetActive(true);
            if (type != null)
                deathVars.FindFsmBool(type).Value = true;
        }

        public static void killCustom(string en, string fi)
        {
            kill();
            death.transform.Find("GameOverScreen/Paper/Fatigue/TextEN").GetComponent<TextMesh>().text = en;
            death.transform.Find("GameOverScreen/Paper/Fatigue/TextFI").GetComponent<TextMesh>().text = fi;
        }
    }
}
