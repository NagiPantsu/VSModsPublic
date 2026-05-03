# Ice Is Cellar

Vintage Story mod that adds Ice Bricks and lets configured blocks contribute to cellar cooling. When enough cooling walls in a cellar use those materials, the mod applies an additional perish-temperature bonus on top of the normal cellar effect.

## Notes

- `iceiscellar:icebricks` is a stable, non-melting cooling block crafted from vanilla harvested glacier ice.
- `iceiscellarmodconfig.json` controls which exact block codes and block-code patterns receive the custom `IceCooling` behavior during asset finalization.
- The custom ice-cellar perish override now applies a reduced sunlight penalty before the cellar and ice adjustments. Dark enclosed rooms still perform best, but the penalty is intentionally weaker than vanilla so ice cellars keep a stronger identity.
- `iceiscellarmodconfig.json` includes `LightPenaltyMultiplier`, which lets users tune how much sunlight affects the custom override. `0` disables the custom light penalty, `0.5` is the default midpoint, and `1` uses the full configured penalty strength.
