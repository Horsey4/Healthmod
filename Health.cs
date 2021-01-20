using MSCLoader;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using HutongGames.PlayMaker;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace HealthMod
{
    public class Health : Mod
    {
        public override string ID => "Health";
        public override string Name => "Health";
        public override string Author => "Horsey4";
        public override string Version => "1.2.1";
        public override bool SecondPass => true;
        public static int apiVer => 3;

        internal string saveFile => $@"{Application.persistentDataPath}\{ID}.dat";
        internal FsmFloat drunk => FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
        internal FsmString vehicle => FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
        internal FsmFloat fatigue => FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
        internal FsmFloat burns => FsmVariables.GlobalVariables.FindFsmFloat("PlayerBurns");
        internal FsmBool sleeping => FsmVariables.GlobalVariables.FindFsmBool("PlayerSleeps");

        internal Settings crashHpLoss;
        internal Settings difficulty;
        internal Settings minCrashSpeed;
        internal ConfigurableJoint vehiJoint;
        internal GameObject death;
        internal List<FsmFloat> deathSpeeds = new List<FsmFloat>();
        internal FsmFloat[] stats =
        {
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerThirst"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerHunger"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerUrine")
        };
        internal Settings vanillaMode;
        internal Material hudMat;
        internal Transform HUD;
        internal Transform hpBar;
        internal BlurOptimized damageEffect;
        internal PlayMakerFSM wiringFsm;
        internal PlayMakerFSM callFsm;
        internal PlayMakerFSM swimFsm;
        internal FsmVariables deathVars;
        internal FsmFloat wasp;
        internal float difficultyMulti;
        internal float pHunger;
        internal float crashMulti;
        internal float crashCooldown;
        internal float crashMin;
        internal float oldForce;
        internal int sleepCounter;

        /// <summary>The sound effects played when the damage() is ran</summary>
        public AudioClip[] hitSfx = new AudioClip[8];
        /// <summary>PLAYER GameObject's Transform</summary>
        public Transform player;
        /// <summary>The amount of health the player has</summary>
        /// <remarks>Clamped to 0-100 by editHp()</remarks>
        public float hp;
        /// <summary>The amount of damage waiting to be dealt as alcohol poisoning</summary>
        /// <remarks>1 poisonCounter = -0.001 hp</remarks>
        public int poisonCounter;
        /// <summary>If vanilla mode is active</summary>
        /// <remarks>Disable your integration if this is true</remarks>
        public bool mode;

        /* Changelogs
         * Fixed rally cars not damaging you
         * Optimized sleep healing
         * Optimized save system
         * Privated some variables that shouldn't be messed with
         */

        public override void OnNewGame() => File.Delete(saveFile);

        public override void OnSave()
        {
            var b1 = BitConverter.GetBytes(hp);
            var b2 = BitConverter.GetBytes(poisonCounter);
            var bytes = new byte[8];
            b1.CopyTo(bytes, 0);
            b2.CopyTo(bytes, 4);
            File.WriteAllBytes(saveFile, bytes);
        }

        public override void ModSettings()
        {
            vanillaMode = new Settings("vanillaMode", "Vanilla Mode", false);
            crashHpLoss = new Settings("crashHpLoss", "Crash Damage", true, updateSettings);
            difficulty = new Settings("difficulty", "Difficulty Multiplier (x)", 1f, updateSettings);
            minCrashSpeed = new Settings("minCrashSpeed", "Minimum Crash Speed (km/h)", 20, updateSettings);

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
            mode = (bool)vanillaMode.Value;
            
            if (!mode)
            {
                log("Loading Stage 2");

                // Non vanilla mode variables
                pHunger = stats[1].Value;
                damageEffect = camera.gameObject.AddComponent<BlurOptimized>();
                deathVars = death.GetComponent<PlayMakerFSM>().FsmVariables;

                // FSM setup
                FsmVariables.GlobalVariables.FindFsmBool("OptionsPermaDeath").Value = false;
                var actions = waspFsm.FsmStates.FirstOrDefault(x => x.Name == "Allergy").Actions;
                var audio = GameObject.Find("MasterAudio/PlayerMisc").transform;
                var room = GameObject.Find("YARD/Building/LIVINGROOM");
                actions[1].Enabled = false; // Disable vignetting for wasp stings
                actions[2].Enabled = false;
                actions[3].Enabled = false;
                wiringFsm = GameObject.Find("SATSUMA(557kg, 248)/Wiring").GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Shock");
                wiringFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;
                if (room)
                {
                    callFsm = room.transform.Find("Telephone/Logic/Ring").GetComponent<PlayMakerFSM>();
                    callFsm.gameObject.SetActive(true);
                    if (!callFsm.transform.parent.gameObject.activeSelf)
                    {
                        callFsm.transform.parent.gameObject.SetActive(true);
                        callFsm.transform.parent.gameObject.SetActive(false);
                    }
                    callFsm.gameObject.SetActive(false);
                    callFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;
                }
                swimFsm = player.GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Swim");
                var state = swimFsm.FsmStates.FirstOrDefault(x => x.Name == "Randomize");
                state.Transitions[1].FsmEvent = swimFsm.FsmEvents.FirstOrDefault(x => x.Name == "SWIM");
                state.Actions[0].Enabled = false;

                // Component Setup
                camera.Find("Drink/Hand/SpiritBottle").gameObject.AddComponent<DrinkListener>().mod = this;
                camera.Find("Drink/Hand/BoozeBottle").gameObject.AddComponent<DrinkListener>().mod = this;
                camera.Find("Drink/Hand/ShotGlass").gameObject.AddComponent<DrinkListener>().mod = this;
                camera.Find("Drink/Hand/BeerBottle").gameObject.AddComponent<DrinkListener>().mod = this;
                camera.Find("DeathBee").gameObject.AddComponent<BeeListener>().mod = this;
                GameObject.Find("MAP").transform.Find("CloudSystem/Clouds/Thunder/GroundStrike").gameObject.AddComponent<LightningListener>().mod = this;

                // Damage setup
                for (var i = 13; i < 21; i++)
                    hitSfx[i - 13] = audio.GetChild(i).GetComponent<AudioSource>().clip; // Get damage sfx
                damageEffect.blurShader = Shader.Find("Hidden/FastBlur");
                damageEffect.blurIterations = 2;
                damageEffect.downsample = 0;
                damageEffect.blurSize = 0;
            }
            try
            {
                var oldSave = $@"{ModLoader.GetModConfigFolder(this)}\save.txt";
                if (File.Exists(oldSave))
                {
                    var saveData = Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(oldSave))).Split(',');
                    var b1 = BitConverter.GetBytes(float.Parse(saveData[0]));
                    var b2 = BitConverter.GetBytes(int.Parse(saveData[1]));
                    var oldBytes = new byte[8];
                    b1.CopyTo(oldBytes, 0);
                    b2.CopyTo(oldBytes, 4);
                    File.WriteAllBytes(saveFile, oldBytes);
                    File.Delete(oldSave);
                } // Convert to new save
                var bytes = File.ReadAllBytes(saveFile);
                editHp(BitConverter.ToSingle(bytes, 0), "Load", true);
                poisonCounter = BitConverter.ToInt32(bytes, 4);
            }
            catch (Exception e) { editHp(100, $"LoadFallback\n{e}", true); } // Load the save file
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
                            transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y - 0.2f, transform.localPosition.z);
                            transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, transform.localScale.z + 0.4f);
                        }
                        else if (cars[i].name == "POLSA")
                        {
                            cars[i].transform.Find("DeathColl").localPosition = Vector3.zero;
                        }
                        else if (cars[i].name.Contains("RALLYCAR"))
                        {
                            var transform = cars[i].transform.GetChild(2);
                            transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y - 0.1f, transform.localPosition.z);
                        }
                    } // Fix the AI collider jank

                    cars[i].gameObject.AddComponent<CrashListener>().mod = this;
                    var fsms = cars[i].GetComponents<PlayMakerFSM>();
                    if (cars[i].transform.parent != null && fsms.Length > 0)
                    {
                        for (var x = 0; x < fsms.Length; x++)
                            if (fsms[x].FsmName.Contains("Throttle")) deathSpeeds.Add(fsms[x].FsmVariables.FindFsmFloat("DeathSpeedMPS"));
                    }
                    else
                    {
                        var trigger = cars[i].GetComponentsInChildren<Collider>().FirstOrDefault(x => x.isTrigger && x.name == "DriveTrigger");

                        if (!trigger) continue;
                        trigger.gameObject.AddComponent<SeatBeltListener>().mod = this;
                    }
                }
            }
            updateSettings();
        }

        public override void FixedUpdate()
        {
            if (death.activeSelf) return;
            if (!mode && (bool)crashHpLoss.Value)
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

            if (death.activeSelf)
            {
                hp = 100;
                poisonCounter = 0;
                return;
            }
            if (mode)
            {
                editHp(100 - Mathf.Max(stats[0].Value - 100, stats[1].Value - 100, stats[2].Value - 100, stats[3].Value - 100, burns.Value, wasp.Value) - hp, null, true);
                return;
            }

            // HP Change
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
            if (wasp.Value > 0)
            {
                if (damage(wasp.Value * 20, "Wasp", 0.05f)) kill("Wasp");
                wasp.Value = 0;
            }
            if (wiringFsm.ActiveStateName == "Random")
            {
                wiringFsm.SendEvent("OFF");
                if (damage(50, "Electrocute", 0.5f)) kill("Electrocute");
            }
            if (callFsm && callFsm.ActiveStateName == "Random")
            {
                callFsm.SendEvent("SURVIVE");
                if (damage(50, "PhoneThunder", 0.5f)) kill("PhoneThunder");
            }
            if (swimFsm.ActiveStateName == "Randomize")
            {
                swimFsm.SendEvent("SWIM");
                if (editHp(-5, "DrunkDrown")) kill("DrunkDrown");
            }
        }

        internal void updateSettings()
        {
            difficultyMulti = Mathf.Clamp(Convert.ToSingle(difficulty.Value), 0.5f, 3);
            crashMin = Mathf.Clamp(Convert.ToSingle(minCrashSpeed.Value), 10, 30) * 5 / 18;

            log($"Settings updated:\n\tcrashHpLoss = {crashHpLoss.Value}\n\tdifficultyMulti = {difficultyMulti}\n\tcrashMin = {crashMin}");

            if (deathSpeeds != null && !mode) for (var i = 0; i < deathSpeeds.Count; i++)
                deathSpeeds[i].Value = (bool)crashHpLoss.Value ? Mathf.Infinity : 5;
        }

        internal void log(object obj) => Console.WriteLine($"[{ID}] {obj}");

        /// <summary>Add or remove hp</summary>
        /// <remarks>noMulti bypasses the difficulty damage multiplier and should not be used</remarks>
        /// <returns>Returns true when the player is at 0 hp</returns>
        public bool editHp(float val, string reason = null, bool noMulti = false)
        {
            if (death.activeInHierarchy) return false;
            if (!noMulti) val = val > 0 ? val / difficultyMulti : val * difficultyMulti;
            hp = Mathf.Clamp(hp + val, 0, 100);
            hpBar.localScale = new Vector3(hp / 100, 1);
            if (reason != null) log($"HP set to {hp} because {reason}");

            if (poisonCounter > 0) hudMat.color = Color.green;
            else hudMat.color = hp > 30 ? Color.white : Color.red;
            return hp == 0;
        }

        /// <summary>Blur the player's vision, play a damage sound, and remove hp</summary>
        /// <remarks>Damage is calculated with -val * damageMulti</remarks>
        /// <returns>Returns true when the player is at 0 hp</returns>
        public bool damage(float val, string reason = null, float damageMulti = 1)
        {
            damageEffect.enabled = true;
            damageEffect.blurSize += val / 10;
            AudioSource.PlayClipAtPoint(hitSfx[UnityEngine.Random.Range(0, hitSfx.Length - 1)], player.transform.position);

            if (reason != null) log($"Blurred because {reason}");
            if (damageMulti == 0) return false;
            return editHp(-val * damageMulti, reason);
        }

        /// <summary>Kill the player using any of the vanilla death booleans</summary>
        public void kill(string type = null)
        {
            death.SetActive(true);
            if (type != null)
                deathVars.FindFsmBool(type).Value = true;
        }

        /// <summary>Kill the player and set custom death text</summary>
        public void killCustom(string en, string fi)
        {
            kill();
            death.transform.Find("GameOverScreen/Paper/Fatigue/TextEN").GetComponent<TextMesh>().text = en;
            death.transform.Find("GameOverScreen/Paper/Fatigue/TextFI").GetComponent<TextMesh>().text = fi;
        }
    }
}
