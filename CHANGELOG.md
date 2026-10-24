# Changelog

## 0.3.0-preview.1 — 2026-10-24
- Paid home-port industry: 3,000 bonds for level one (120% output), then 5,000 for level two (144%). All nine resource outputs scale; each port has independent levels and purchases require confirmation.
- Campaign-local optional manager data persists levels and exact fractional ammunition remainders. Legacy campaigns start at zero. Native save/load method bodies and Port serialization remain untouched.
- Dynamic purchase card appears only for an owned home port on the Bridge screen; module toggle pauses bonuses/purchases without losing levels or refunding bonds. AI does not buy upgrades automatically; upgrades remain attached to a captured port.
- Original stock capacities, transfers, starting inventories, war-bond income and 125% Medium construction settings are preserved.
- Native Unity tests cover purchases, rounding, real manufacturing calls, campaign serialization, restart and loading an extended save with the four Community Mods DLLs disabled. Experimental status remains.

## 0.2.0-preview.2 — 2026-10-24
- Optional Normal/Medium enemy ship construction balance: 125% speed by default instead of vanilla 150%; live toggle restores vanilla. Guarded by game assembly and method IL hashes. Other difficulties, player construction and income are unchanged.
- Scrollable community section fits the native Modifications screen's right column, with readable text, a visible scrollbar and enough height for status lines.
- 118th Russian override clarifies that supply fleets per port is an average fleet count, not war-bond income.
- Installer refuses duplicate known plugin assemblies even if renamed. Loader, config and unrelated files remain preserved; temporary cleanup validates owned absolute paths.
- Reproducible isolated Unity/Mono validation with results bound to exact DLL hashes. Historical claims are separated from current verification; releaseReady remains false.

## 0.2.0-preview.1 — 2026-10-24
- Clean portable source project based on guarded R1–R5 Hotfix, Scout Patrol 0.1.0 and Interface Tools 0.1.2.
- Minimal child section in native UIModOptions, independent controls without fake stock mod profiles.
- Actual hotfix state versus requested restart state; existing cfg names retained.
- Live patrol, convoy and RU lifecycle APIs with owner-only Harmony cleanup.
- Transactional plugin-only packaging/install/update/rollback/remove with hash ownership and closed-game checks.
- Experimental: runtime coverage is incomplete; releaseReady remains false. See README and runtime validation report.
