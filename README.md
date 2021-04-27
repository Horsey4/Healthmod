# Healthmod

Adds a robust health system to My Summer Car

API versions proceeding 4 (v1.2.3 or above) have fully static varaibles (as only 1 instance should be loaded).

If you plan to make healthmod an optional add-on, do NOT use `using HealthMod` and instead access the classes via namespace so long as the mod is installed.

If you want to require healhmod, you can add the `using`, as modloader won't recognise the mod if there is a reference to a non-existent file.

# Example

```cs
public class ExampleInterface : Mod
{
    public override string ID => "ExampleInterface";
    public override string Name => "ExampleInterface";
    public override string Author => "Horsey4";
    public override string Version => "1.0.0";
    bool healthmodInstalled;

    public override void OnLoad()
    {
        if (ModLoader.IsModPresent("Health"))
        {
            if (HealthMod.Health.apiVer < 6)
                // Check if healthmod is up to date
                ModConsole.Warning($"[{ID}] HealthMod out of date or disabled, skipped hooks");
            else healthmodInstalled = true; // Set the bool to indicate healthmod is installed and updated
        }
    }

    public override void Update()
    {
        if (healthmodInstalled && conditionIsMet)
            HealthMod.Health.editHp(-10); // Remove 10hp if said condition is met
    }
}
```

# Variables

| Variable | Type | Description |
|-|-|-|
| death | GameObject | Reference to `Systems/Death` |
| player | Transform | Reference to `PLAYER` |
| wasp | FsmFloat | Reference to `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera` Blindness FSM `MaxAllergy` float |
| vehiJoint | ConfigurableJoint | Reference to the joint managing death force for the current vehicle |
| stats | FsmFloat[] | List of player global stat variables; Thirst, Hunger, Stress, & Urine |
| drunk | FsmFloat | Global unadjusted drunkness variable |
| fatigue | FsmFloat | Global variable for player fatigue |
| burns | FsmFloat | Global variable for player burns |
| vehicle | FsmString | Global variable with the name of the player's current vehicle |
| sleeping | FsmBool | Global variable if the player is sleeping or not |
| crashDamage | bool | Whether or not the player has crash damage enabled |
| mode | bool | Whether or not the player has vanilla mode enabled |
| hitSfx | AudioClip[] | Audio randomly selected to be played when damaged |
| hp | float | The player's current health |
| poisonCounter | int | Milliseconds of alcohol poisoning left |

# Methods

| Method | Type | Returns | Description |
|-|-|-|-|
| editHp | bool | If the player is dead or not | Simply add `val` hp (can be negitive) |
| damage | bool | If the player is dead or not | Subtract `val` hp, blur the player's vision, & play a random damage sfx |
| kill | void | N/A | Kill the player |
| killCustom | void | N/A | Kill the player and set custom death text |
