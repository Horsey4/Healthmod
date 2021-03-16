using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using System.Linq;

namespace HealthMod
{
    class CrashListener : MonoBehaviour
    {
        Rigidbody thisRb;
        Vector3 velo;

        void Awake() => thisRb = GetComponent<Rigidbody>();

        void OnCollisionEnter(Collision col)
        {
            if (!Health.death.activeSelf && Health.crashDamage)
            {
                if (Health.crashCooldown > 0) return;

                var hitSpeed = Vector3.Distance(thisRb.velocity, velo);
                if (hitSpeed < Health.crashMin) return;
                Health.crashCooldown = hitSpeed;

                if (Health.vehicle.Value != "" && name.ToUpper().Contains(Health.vehicle.Value.ToUpper())
                    && Health.damage(hitSpeed * Health.crashMulti, 1)) Health.vehiJoint.breakTorque = 0;
            }
        }

        void FixedUpdate() => velo = thisRb.velocity;
    }

    class DrinkListener : MonoBehaviour
    {
        public int drinkMulti;

        void FixedUpdate() { if (Health.drunk.Value > 4) Health.poisonCounter += drinkMulti; }
    }

    class SeatBeltListener : MonoBehaviour
    {
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
            if (Health.vehicle.Value != "" && transform.root.name.Contains(Health.vehicle.Value.ToUpper()) && Health.oldForce != force.Value)
                Health.oldForce = force.Value;
        }
    }

    class BeeListener : MonoBehaviour
    {
        void OnEnable()
        {
            AudioSource.PlayClipAtPoint(GetComponent<AudioSource>().clip, transform.position);
            if (!Health.death.activeSelf && Health.damage(2000, 0.015f))
                Health.kill("DriveBee");
            gameObject.SetActive(false);
        }
    }

    class LightningListener : MonoBehaviour
    {
        PlayMakerFSM lightningFsm;

        void Awake()
        {
            lightningFsm = GetComponent<PlayMakerFSM>();
            var state = lightningFsm.FsmStates.FirstOrDefault(x => x.Name == "Kill");
            state.Actions[0].Enabled = false;
            state.Actions[1].Enabled = false;
            state.Transitions[0].FsmEvent = lightningFsm.FsmEvents.FirstOrDefault(x => x.Name == "DIE");
        }

        void Update()
        {
            if (lightningFsm.ActiveStateName == "Kill")
            {
                if (Health.damage(100, 0.5f))
                    Health.kill("Lightning");
                lightningFsm.SendEvent("DIE");
            }
        }
    }

    class FireListener : MonoBehaviour
    {
        void Awake()
        {
            var col = gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.4f, 0.4f);
            col.size = new Vector3(1.5f, 1, 2.5f);
        }

        void OnTriggerStay(Collider col)
        {
            if (col.transform == Health.player && Health.editHp(-0.18f, "Burn")) Health.kill("Burn");
        }
    }
}