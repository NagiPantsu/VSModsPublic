# Ice Is Cellar

Vintage Story mod that makes packed glacier ice and selected wood blocks contribute to cellar cooling. When enough cooling walls in a cellar use those materials, the mod applies an additional perish-temperature bonus on top of the normal cellar effect.

## Notes

- `packedglacierice` is patched to use `lightAbsorption: 99` because the vanilla block ships with `lightAbsorption: 0`, which prevents it from behaving like a normal full cellar wall for cellar-likeness calculations.
- The mod also patches the target block families with the custom `IceCooling` behavior so they count as cooling materials during room evaluation.
- The custom ice-cellar perish override now applies a reduced sunlight penalty before the cellar and ice adjustments. Dark enclosed rooms still perform best, but the penalty is intentionally weaker than vanilla so ice cellars keep a stronger identity.
- `coolingmodconfig.json` includes `LightPenaltyMultiplier`, which lets users tune how much sunlight affects the custom override. `0` disables the custom light penalty, `0.5` is the default midpoint, and `1` uses the full configured penalty strength.
