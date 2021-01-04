using MSCLoader;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace HealthMod
{
    public class Health : Mod
    {
        public override string ID => "Health";
        public override string Name => "Health";
        public override string Author => "Horsey4";
        public override string Version => "1.1.2";
        public override bool SecondPass => true;
        public string saveFile => $@"{ModLoader.GetModConfigFolder(this)}\save.txt";
        public FsmFloat drunk => FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
        public FsmString vehicle => FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
        public FsmFloat fatigue => FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
        public FsmFloat burns => FsmVariables.GlobalVariables.FindFsmFloat("PlayerBurns");
        public Settings vanillaMode;
        public Settings crashHpLoss;
        public Settings difficulty;
        public Settings minCrashSpeed;
        public ConfigurableJoint vehiJoint;
        public GameObject death;
        public VignetteAndChromaticAberration damageEffect;
        public List<FsmFloat> deathSpeeds = new List<FsmFloat>();
        public AudioClip[] hitSfx = new AudioClip[8];
        public Material hudMat;
        public Transform HUD;
        public Transform hpBar;
        public Transform player;
        public PlayMakerFSM wiringFsm;
        public PlayMakerFSM callFsm;
        public PlayMakerFSM swimFsm;
        public FsmVariables deathVars;
        public FsmFloat[] stats =
        {
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerThirst"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerHunger"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerUrine")
        };
        public FsmFloat wasp;
        public float hp;
        public float crashMulti;
        public float crashCooldown;
        public float crashMin;
        public int poisonCounter;
        public float oldForce;
        public float pHunger;
        public int sleepCounter;
        float difficultyMulti;
        bool mode;

        /* Changelogs
         * Fixed the Tangerine (and possibly other modded vehicles) not being hooked
         */

        public override void OnNewGame() => File.Delete(saveFile);

        public override void OnSave() => File.WriteAllText(saveFile, Convert.ToBase64String(Encoding.UTF8.GetBytes
        (
            string.Join(",", new string[]
            {
                hp.ToString(),
                poisonCounter.ToString()
            })
        )));

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
            log("Loading Stage 1");

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
                mode = (bool)vanillaMode.Value;
                damageEffect = camera.GetComponent<VignetteAndChromaticAberration>();
                deathVars = death.GetComponent<PlayMakerFSM>().FsmVariables;

                // FSM setup
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
                damageEffect.blur = Mathf.Infinity;
                damageEffect.blurSpread = 0;
            }
            try
            {
                var saveData = Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(saveFile))).Split(',');
                editHp(float.Parse(saveData[0]), "Load", true);
                poisonCounter = int.Parse(saveData[1]);
            }
            catch (Exception e) { editHp(100, $"LoadFallback\n{e}", true); } // Load the save file
        }

        public override void SecondPassOnLoad()
        {
            log("Loading Stage 3");

            // Fix HUD with other mods
            var offset = 0f;
            if (ModLoader.IsModPresent("Alcohol_Meter"))
            {
                HUD.Find("Alcohol").localPosition = ModLoader.IsModPresent("dirtmenag")
                    ? new Vector3(-11.5f, 6.8f) : new Vector3(-11.5f, 6.4f);
                offset += 0.4f;
            }
            if (ModLoader.IsModPresent("Playtime_and_clock_in_HUD"))
            {
                if (HUD.Find("Playtime").gameObject.activeSelf) offset += 0.4f;
                if (HUD.Find("Clock").gameObject.activeSelf) offset += 0.4f;
            }
            if (ModLoader.IsModPresent("dirtmenag")) offset -= 0.4f;
            HUD.Find("Money").localPosition = new Vector3(-11.5f, 6.4f - offset);
            HUD.Find("Jailtime").localPosition = new Vector3(-11.5f, 6 - offset);

            // Other setup
            var cars = Resources.FindObjectsOfTypeAll<Drivetrain>();
            var colls = Resources.FindObjectsOfTypeAll<Collider>();
            var helmetOn = FsmVariables.GlobalVariables.FindFsmBool("PlayerHelmet");
            for (var i = 0; i < cars.Length; i++)
            {
                log($"Hooking {cars[i].name}, Root = {cars[i].transform.parent == null}");
                cars[i].gameObject.AddComponent<CrashListener>().mod = this;
                var fsms = cars[i].GetComponents<PlayMakerFSM>();
                if (cars[i].transform.parent != null && fsms.Length > 0)
                {
                    for (var x = 0; x < fsms.Length; x++)
                        if (fsms[x].FsmName.Contains("Throttle")) deathSpeeds.Add(fsms[x].FsmVariables.FindFsmFloat("DeathSpeedMPS"));
                }
                else
                {
                    var joint = cars[i].GetComponentInChildren<ConfigurableJoint>();
                    Component trigger;
                    if (!joint)
                    {
                        trigger = cars[i].transform.Find("LOD/PlayerTrigger/DriveTrigger");
                        if (!trigger) trigger = cars[i].transform.Find("PlayerTrigger/DriveTrigger");
                    }
                    else trigger = joint;

                    if (!trigger)
                    {
                        log("Failed");
                        continue;
                    }
                    var fsm = trigger.GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "HeadForce");
                    if (!fsm) continue;

                    (fsm.FsmStates[0].Actions[2] as SetProperty).everyFrame = false;
                    (fsm.FsmStates[0].Actions[3] as SetProperty).everyFrame = false;
                    (fsm.FsmStates[1].Actions[2] as SetProperty).everyFrame = false;
                    (fsm.FsmStates[1].Actions[3] as SetProperty).everyFrame = false;
                } // Disable setting of joint strength everyframe
            }
            for (var i = 0; i < colls.Length; i++)
                if (colls[i].name == "SleepTrigger") colls[i].gameObject.AddComponent<SleepListener>().mod = this;
            updateSettings();
        }

        public override void FixedUpdate()
        {
            // De-blur
            if (!mode && damageEffect.blurSpread > 0)
                damageEffect.blurSpread -= 0.1f;
            else damageEffect.enabled = false;

            if (death.activeSelf)
            {
                hp = 100;
                poisonCounter = 0;
                return;
            }
            if (mode)
            {
                editHp(100 - Mathf.Max(stats[0].Value - 100, stats[1].Value - 100, stats[2].Value - 100, stats[3].Value - 100, burns.Value, wasp.Value) - hp, "VanillaMode", true);
                return;
            }

            // HP Change
            if ((bool)crashHpLoss.Value)
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
            for (var i = 0; i < stats.Length; i++)
                if (stats[i].Value > 100.1f)
                {
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
            if (poisonCounter > 0)
            {
                poisonCounter--;
                if (editHp(-0.001f, "Poison"))
                {
                    kill();
                    death.transform.Find("GameOverScreen/Paper/Fatigue/TextEN").GetComponent<TextMesh>().text = "Man killed\nfrom alcohol\npoisoning";
                    death.transform.Find("GameOverScreen/Paper/Fatigue/TextFI").GetComponent<TextMesh>().text = "Mies kuoli\nalkoholimyrkytykseen";
                }
            }
            if (pHunger != stats[1].Value)
            {
                var diff = pHunger - stats[1].Value;
                if (diff > 0) editHp(diff, "Eat");
                pHunger = stats[1].Value;
            }
        }

        void updateSettings()
        {
            log("Settings updated:");
            log($"crashHpLoss = {crashHpLoss.Value}");
            difficultyMulti = Mathf.Clamp(Convert.ToSingle(difficulty.Value), 0.5f, 3);
            log($"difficultyMulti = {difficultyMulti}");
            crashMin = Mathf.Clamp(Convert.ToSingle(minCrashSpeed.Value), 10, 30) * 5 / 18;
            log($"crashMin = {crashMin}");

            if (deathSpeeds != null && !mode) for (var i = 0; i < deathSpeeds.Count; i++)
                deathSpeeds[i].Value = (bool)crashHpLoss.Value ? Mathf.Infinity : 5;
        }

        void log(object str) => Console.WriteLine($"[{ID}] {str}");

        public bool editHp(float val, string reason, bool noMulti = false)
        {
            if (!noMulti)
                val = val > 0 ? val / difficultyMulti : val * difficultyMulti;
            hp = Mathf.Clamp(hp + val, 0, 100);
            hpBar.localScale = new Vector3(hp / 100, 1);
            log($"HP set to {hp} because {reason}");

            if (poisonCounter > 0) hudMat.color = Color.green;
            else hudMat.color = hp > 30 ? Color.white : Color.red;
            return hp == 0;
        }

        public bool damage(float val, string reason, float damageMulti = 1)
        {
            damageEffect.enabled = true;
            damageEffect.blurSpread += val / 10;
            AudioSource.PlayClipAtPoint(hitSfx[UnityEngine.Random.Range(0, hitSfx.Length - 1)], player.transform.position);

            log($"Blurred because {reason}");
            return damageMulti == 0 || editHp(-val * damageMulti, reason);
        }

        public void kill(string type = null)
        {
            death.SetActive(true);
            if (type != null)
                deathVars.FindFsmBool(type).Value = true;
        }
    }
}
