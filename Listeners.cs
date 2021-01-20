using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using System.Linq;

namespace HealthMod
{
    public class CrashListener : MonoBehaviour
    {
        Rigidbody thisRb => GetComponent<Rigidbody>();
        public Health mod;
        Vector3 velo;

        void OnCollisionEnter(Collision col)
        {
            if (!mod.death.activeSelf && (bool)mod.crashHpLoss.Value)
            {
                if (mod.crashCooldown > 0) return;

                var hitSpeed = Vector3.Distance(thisRb.velocity, velo);
                if (hitSpeed < mod.crashMin) return;
                mod.crashCooldown = hitSpeed;

                if (transform.parent && col.gameObject.name == "PLAYER")
                {
                    if (mod.damage(hitSpeed * 8, "AICrash"))
                    {
                        if (name.Contains("RALLY")) mod.kill("RunOverRally");
                        else if (name.Contains("drag")) mod.kill("RunOverDrag");
                        else mod.kill("RunOver");
                    }
                }
                else if (mod.vehicle.Value != "" && name.ToUpper().Contains(mod.vehicle.Value.ToUpper())
                    && mod.damage(hitSpeed * mod.crashMulti, "Crash")) mod.vehiJoint.breakTorque = 0;
            }
        }

        void FixedUpdate() => velo = thisRb.velocity;

        /*IEnumerator damage(Collision col)
        {
            var pVelo = velo;
            yield return new WaitForFixedUpdate();
            if (mod.crashCooldown > 0) yield break;

            var hitSpeed = Vector3.Distance(thisRb.velocity, pVelo);
            if (hitSpeed < mod.crashMin) yield break;
            mod.crashCooldown = hitSpeed;

            if (transform.parent && col.gameObject.name == "PLAYER")
            {
                if (mod.damage(hitSpeed * 8, "AICrash"))
                {
                    if (name.Contains("RALLY")) mod.kill("RunOverRally");
                    else if (name.Contains("drag")) mod.kill("RunOverDrag");
                    else mod.kill("RunOver");
                }
            }
            else if (mod.vehicle.Value != "" && name.ToUpper().Contains(mod.vehicle.Value.ToUpper())
                && mod.damage(hitSpeed * mod.crashMulti, "Crash")) mod.vehiJoint.breakTorque = 0;
        }*/
    }

    public class SeatBeltListener : MonoBehaviour
    {
        public Health mod;
        FsmFloat force;

        void Awake()
        {
            var fsm = GetComponents<PlayMakerFSM>().FirstOrDefault(x => x.FsmName == "HeadForce");
            if (!fsm)
            {
                Destroy(this);
                return;
            }

            force = fsm.FsmVariables.FindFsmFloat("Force");
            (fsm.FsmStates[0].Actions[2] as SetProperty).everyFrame = false;
            (fsm.FsmStates[0].Actions[3] as SetProperty).everyFrame = false;
            (fsm.FsmStates[1].Actions[2] as SetProperty).everyFrame = false;
            (fsm.FsmStates[1].Actions[3] as SetProperty).everyFrame = false;
        }

        void Update()
        {
            if (mod.vehicle.Value != "" && transform.root.name.Contains(mod.vehicle.Value.ToUpper()) && mod.oldForce != force.Value)
                mod.oldForce = force.Value;
        }
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
}
