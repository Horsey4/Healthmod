using UnityEngine;
using System.Linq;

namespace HealthMod
{
    public class CrashListener : MonoBehaviour
    {
        Rigidbody thisRb => GetComponent<Rigidbody>();
        public Health mod;
        float velo;

        void OnCollisionEnter(Collision col)
        {
            if (!mod.death.activeSelf && (bool)mod.crashHpLoss.Value && mod.crashCooldown <= 0)
            {
                var hitSpeed = Mathf.Abs(thisRb.velocity.magnitude - velo);
                if (hitSpeed < mod.crashMin) return;
                mod.crashCooldown = hitSpeed;
                if (!transform.parent)
                {
                    if (mod.vehicle.Value != "" && name.Contains(mod.vehicle.Value.ToUpper()) && mod.damage(hitSpeed * mod.crashMulti, "Crash"))
                    {
                        if (col.gameObject.name == "TRAIN") mod.kill("Train");
                        else mod.vehiJoint.breakTorque = 0;
                    }
                }
                else if (col.transform.root.name == "PLAYER" && mod.damage(hitSpeed * 6, "AICrash"))
                {
                    if (name.Contains("RALLY")) mod.kill("RunOverRally");
                    else if (name.Contains("drag")) mod.kill("RunOverDrag");
                    else mod.kill("RunOver");
                }
            }
        }

        void FixedUpdate() => velo = thisRb.velocity.magnitude;
    }

    public class BeeListener : MonoBehaviour
    {
        public Health mod;

        void OnEnable()
        {
            AudioSource.PlayClipAtPoint(GetComponent<AudioSource>().clip, transform.position);
            if (!mod.death.activeSelf && mod.damage(2000, "Bee", 0.015f))
                mod.kill("DriveBee");
            gameObject.SetActive(false);
        }
    }

    public class DrinkListener : MonoBehaviour
    {
        public Health mod;
        int drinkMulti;

        void Awake()
        {
            switch (name)
            {
                case "SpiritBottle":
                    drinkMulti = 100;
                    break;
                case "BoozeBottle":
                    drinkMulti = 50;
                    break;
                case "ShotGlass":
                    drinkMulti = 30;
                    break;
                case "BeerBottle":
                    drinkMulti = 10;
                    break;
            }
        }

        void FixedUpdate() { if (mod.drunk.Value > 4) mod.poisonCounter += drinkMulti; }
    }

    public class LightningListener : MonoBehaviour
    {
        PlayMakerFSM lightningFsm => GetComponent<PlayMakerFSM>();
        public Health mod;

        void Awake()
        {
            var state = lightningFsm.FsmStates.FirstOrDefault(x => x.Name == "Kill");
            state.Actions[0].Enabled = false;
            state.Actions[1].Enabled = false;
            state.Transitions[0].FsmEvent = lightningFsm.FsmEvents.FirstOrDefault(x => x.Name == "DIE");
        }

        void Update()
        {
            if (lightningFsm.ActiveStateName == "Kill")
            {
                if (mod.damage(100, "Lightning", 0.5f))
                    mod.kill("Lightning");
                lightningFsm.SendEvent("DIE");
            }
        }
    }

    public class SleepListener : MonoBehaviour
    {
        PlayMakerFSM sleepFsm => GetComponent<PlayMakerFSM>();
        public Health mod;

        void FixedUpdate()
        {
            switch (sleepFsm.ActiveStateName)
            {
                case "Get positions":
                    mod.sleepCounter = 0;
                    break;
                case "Sleep":
                    mod.sleepCounter++;
                    if (mod.sleepCounter == 200)
                        mod.editHp(mod.fatigue.Value, "Sleep");
                    break;
            }
        }
    }
}
