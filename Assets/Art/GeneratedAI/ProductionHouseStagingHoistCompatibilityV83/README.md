# ProductionHouseStagingHoistCompatibilityV83

V83 is a two-file stable-base replacement for using V69 structural stages with V81 dynamic hoist ownership. It is not integrated runtime art.

- Exterior is byte-identical to the audited V81 no-suspended-load base.
- Interior removes the same old baked rope, hook and empty net with the same V81 mask.
- All V69 owner sprites, six-stage paths, crops, offsets and V38 anchors remain external references and are not duplicated.
- Replace both V69 stable bases atomically; never stack V69, V81 and V83 stable bases.
- Rebuild with `python Tools/build_v83_house_staging_hoist_compatibility.py`.
