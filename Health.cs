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
        public override string Version => "1.0.1";
        public override bool SecondPass => true;
        public string saveFile => $@"{ModLoader.GetModConfigFolder(this)}\save.txt";
        public FsmFloat drunk => FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
        FsmString vehicle => FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
        FsmFloat fatigue => FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
        FsmFloat burns => FsmVariables.GlobalVariables.FindFsmFloat("PlayerBurns");
        VignetteAndChromaticAberration damageEffect;
        ConfigurableJoint vehiJoint;
        CrashListener[] listeners;
        GameObject death;
        AudioClip[] hitSfx;
        Material hudMat;
        Transform HUD;
        Transform hpBar;
        Transform player;
        PlayMakerFSM[] sleepFsms;
        PlayMakerFSM wiringFsm;
        PlayMakerFSM callFsm;
        FsmVariables deathVars;
        FsmFloat[] stats =
        {
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerThirst"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerHunger"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress"),
            FsmVariables.GlobalVariables.FindFsmFloat("PlayerUrine")
        };
        FsmFloat[] deathSpeeds;
        FsmFloat wasp;
        Settings vanillaMode;
        Settings crashHpLoss;
        Settings difficulty;
        Settings minCrashSpeed;
        public float hp;
        public int poisonCounter;
        float crashMulti;
        float crashCooldown;
        float oldForce;
        float pHunger;
        float difficultyMulti;
        float crashMin;
        int sleepCounter;
        bool mode;

        public override void OnNewGame()
        {
            try { File.Delete(saveFile); }
            catch (Exception e)
            {
                error($"Could not reset {saveFile}\n{e.Message}");
                log($"Reset failed\n{e}");
            }
        }

        public override void OnSave()
        {
            try
            {
                File.WriteAllText(saveFile, Convert.ToBase64String(Encoding.UTF8.GetBytes
                (
                    string.Join(",", new string[]
                    {
                        hp.ToString(),
                        poisonCounter.ToString()
                    })
                )));
            }
            catch (Exception e)
            {
                error($"Could not save {saveFile}\n{e.Message}");
                log($"Save failed\n{e}");
            }
        }

        public override void ModSettings()
        {
            vanillaMode = new Settings("vanillaMode", "Vanilla Mode", false);
            crashHpLoss = new Settings("crashHpLoss", "Crash Damage", true, updateSettings);
            difficulty = new Settings("difficulty", "Difficulty Multiplier (x)", 1f, updateSettings);
            minCrashSpeed = new Settings("minCrashSpeed", "Minimum Crash Speed (km/h)", 20, updateSettings);
            updateSettings();

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
                // Non vanilla mode variables
                var colls = Resources.FindObjectsOfTypeAll<Collider>();
                pHunger = stats[1].Value;
                mode = (bool)vanillaMode.Value;
                damageEffect = camera.GetComponent<VignetteAndChromaticAberration>();
                deathVars = death.GetComponent<PlayMakerFSM>().FsmVariables;

                // Damage setup
                var actions = waspFsm.FsmStates.FirstOrDefault(x => x.Name == "Allergy").Actions;
                var audio = GameObject.Find("MasterAudio/PlayerMisc").transform;
                var hitList = new List<AudioClip>();
                var sleepList = new List<PlayMakerFSM>();
                actions[1].Enabled = false; // Disable vignetting for wasp stings
                actions[2].Enabled = false;
                actions[3].Enabled = false;
                camera.Find("Drink/Hand/SpiritBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 100;
                camera.Find("Drink/Hand/BoozeBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 50;
                camera.Find("Drink/Hand/ShotGlass").gameObject.AddComponent<DrinkListener>().drinkMulti = 30;
                camera.Find("Drink/Hand/BeerBottle").gameObject.AddComponent<DrinkListener>().drinkMulti = 10;
                for (var i = 13; i < 21; i++)
                    hitList.Add(audio.GetChild(i).GetComponent<AudioSource>().clip); // Get damage sfx
                hitSfx = hitList.ToArray();
                wiringFsm = GameObject.Find("SATSUMA(557kg, 248)/Wiring").GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "Shock");
                wiringFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;
                callFsm = GameObject.Find("YARD/Building/LIVINGROOM/Telephone/Logic").transform.Find("Ring").GetComponent<PlayMakerFSM>();
                callFsm.gameObject.SetActive(true);
                callFsm.gameObject.SetActive(false);
                callFsm.FsmStates.FirstOrDefault(x => x.Name == "Random").Actions[0].Enabled = false;
                camera.Find("DeathBee").gameObject.AddComponent<BeeListener>();

                // Other setup
                for (var i = 0; i < colls.Length; i++)
                    if (colls[i].name == "SleepTrigger") sleepList.Add(colls[i].GetComponent<PlayMakerFSM>()); // Sleep healing
                sleepFsms = sleepList.ToArray();
                var gifuFsm = GameObject.Find("GIFU(750/450psi)").transform.Find("LOD/PlayerTrigger/DriveTrigger")
                    .GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "HeadForce").FsmStates;
                for (var i = 0; i < gifuFsm.Length; i++)
                {
                    (gifuFsm[i].Actions[2] as SetProperty).everyFrame = false;
                    (gifuFsm[i].Actions[3] as SetProperty).everyFrame = false;
                } // Fix the gifu setting the joint strength every frame
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

            // Hook all vehicles
            var listenerList = new List<CrashListener>();
            var deathSpeedList = new List<FsmFloat>();
            var cars = Resources.FindObjectsOfTypeAll<CarDynamics>();
            for (var i = 0; i < cars.Length; i++)
            {
                listenerList.Add(cars[i].gameObject.AddComponent<CrashListener>());
                var fsms = cars[i].GetComponents<PlayMakerFSM>();
                if (cars[i].transform.parent != null && fsms.Length > 0)
                    for (var x = 0; x < fsms.Length; x++)
                        if (fsms[x].FsmName.Contains("Throttle")) deathSpeedList.Add(fsms[x].FsmVariables.FindFsmFloat("DeathSpeedMPS"));
            }
            listeners = listenerList.ToArray();
            deathSpeeds = deathSpeedList.ToArray();
            updateSettings();
        }

        public override void FixedUpdate()
        {
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

            // HP Loss
            if ((bool)crashHpLoss.Value)
            {
                if (vehicle.Value != "" && (!vehiJoint || vehiJoint && vehiJoint.breakForce != Mathf.Infinity))
                {
                    vehiJoint = player.parent.parent.parent.GetComponent<ConfigurableJoint>();
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
                for (var i = 0; i < listeners.Length; i++)
                    if (listeners[i].hitSpeed > crashMin)
                    {
                        if (listeners[i].playerCar)
                        {
                            if (player.root == listeners[i].transform && damage(listeners[i].hitSpeed * crashMulti, "PlayerCrash"))
                            {
                                switch (listeners[i].hitObj)
                                {
                                    case "TRAIN":
                                        kill("Train");
                                        break;
                                    default:
                                        kill("Crash");
                                        break;
                                }
                            }
                        }
                        else if (damage(listeners[i].hitSpeed * 5, "AICrash"))
                        {
                            if (listeners[i].name.Contains("RALLY")) kill("RunOverRally");
                            else if (listeners[i].name.Contains("drag")) kill("RunOverDrag");
                            else kill("RunOver");
                        }
                        crashCooldown = listeners[i].hitSpeed;
                        listeners[i].hitSpeed = 0;
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
                if (editHp(-burns.Value, "FireDamage")) kill("Burn");
                burns.Value = 0;
            }
            if (wasp.Value > 0)
            {
                if (damage(wasp.Value * 4, "WaspDamage", 0.25f)) kill("Wasp");
                wasp.Value = 0;
            }
            if (wiringFsm.ActiveStateName == "Random")
            {
                wiringFsm.SendEvent("OFF");
                if (damage(50, "Electrocute", 0.5f)) kill("Electrocute");
            }
            if (callFsm.ActiveStateName == "Random")
            {
                callFsm.SendEvent("SURVIVE");
                if (damage(50, "PhoneThunder", 0.5f)) kill("PhoneThunder");
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

            // Healing
            for (var i = 0; i < sleepFsms.Length; i++)
                switch (sleepFsms[i].ActiveStateName)
                {
                    case "Get positions":
                        sleepCounter = 0;
                        break;
                    case "Sleep":
                        sleepCounter++;
                        if (sleepCounter == 200)
                            editHp(fatigue.Value, "SleepHeal");
                        break;
                }
            if (pHunger != stats[1].Value)
            {
                var diff = pHunger - stats[1].Value;
                if (diff > 0) editHp(diff, "EatHeal");
                pHunger = stats[1].Value;
            }

            // De-blur
            if (damageEffect.blurSpread > 0)
                damageEffect.blurSpread -= 0.1f;
            else damageEffect.enabled = false;
        }

        void updateSettings()
        {
            difficultyMulti = Mathf.Clamp(Convert.ToSingle(difficulty.Value), 0.5f, 3);
            crashMin = Mathf.Clamp(Convert.ToSingle(minCrashSpeed.Value), 10, 30) * 5 / 18;

            if (listeners != null) for (var i = 0; i < listeners.Length; i++)
                listeners[i].enabled = (bool)crashHpLoss.Value;
            if (deathSpeeds != null && !mode) for (var i = 0; i < deathSpeeds.Length; i++)
                deathSpeeds[i].Value = (bool)crashHpLoss.Value ? Mathf.Infinity : 5;
        }

        void error(string str) => ModConsole.Error($"[{ID}] {str}");

        void log(string str) => Console.WriteLine($"[{ID}] {str}");

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
            if (type == "Crash" && vehiJoint && vehicle.Value != "Tangerine") GameObject.Destroy(vehiJoint);
            else
            {
                death.SetActive(true);
                if (type != null)
                    deathVars.FindFsmBool(type).Value = true;
            }
        }
    }

    public class CrashListener : MonoBehaviour
    {
        Rigidbody thisRb => GetComponent<Rigidbody>();
        public bool playerCar => transform.parent == null;
        public string hitObj;
        public float hitSpeed;
        float velo;

        void OnCollisionEnter(Collision col)
        {
            if (playerCar ^ col.transform.root.name == "PLAYER")
            {
                hitSpeed = Mathf.Abs(thisRb.velocity.magnitude - velo); // Acceleration
                hitObj = col.gameObject.name;
            }
        }

        void FixedUpdate() => velo = thisRb.velocity.magnitude;
    }

    public class BeeListener : MonoBehaviour
    {
        Health mod => ModLoader.LoadedMods.FirstOrDefault(x => x.ID == "Health") as Health;

        void Update()
        {
            AudioSource.PlayClipAtPoint(GetComponent<AudioSource>().clip, transform.position);
            if (mod.damage(2000, "Bee", 0.015f))
                mod.kill("DriveBee");
            gameObject.SetActive(false);
        }
    }

    public class DrinkListener : MonoBehaviour
    {
        Health mod => ModLoader.LoadedMods.FirstOrDefault(x => x.ID == "Health") as Health;
        public int drinkMulti;

        void FixedUpdate()
        {
            if (mod.drunk.Value > 4) mod.poisonCounter += drinkMulti;
        }
    }
}