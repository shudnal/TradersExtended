# Repository review: trading focus, whole-lot amounts, and runtime safety

Review completed on 2026-09-08.

## Baseline and scope

Reviewed master `54e017fe8b40fa3d6feb0a4fc68713a410063bfb`. Changes are confined to
`fix/trade-focus-stack-dialog-review`; the existing user discussion is intentionally not linked or modified.
The package/mod version remains `2.0.0`. No generated distribution DLL or ZIP is claimed to be rebuilt by this change.

The review covers all 19 original C# files, the project/reference and ILRepack setup, both manual publishing scripts,
manifest update scripts, README/config examples, and distribution metadata. Game-facing assumptions were checked first
against `shudnal/assemblies_combined` (`assembly_valheim/StoreGui.cs`, `Inventory.cs`, `ItemDrop.cs`, `ZPackage.cs`, `ZRpc.cs`,
`ZRoutedRpc.cs`, and `ZNet.cs`). In particular, native inventory addition is allowed to mutate stacks before returning failure.

## Recovery of the interrupted work

The interrupted branch contained seven compressed patch fragments, not applied source changes. Their SHA-256
`d69a8be3b86df010a94cf020f5b7c23bb40a81f4f11d23cdecd9abe7b8282fbd` was verified before recovery.
Its original workflow stopped at the manifest hunk, before committing source files or running its regression project.
The patch was recovered for inspection, then corrected rather than accepted as previously validated work.
The temporary import files and recovery workflow are removed from the final branch tree.

## Confirmed findings and corrections

| Area | Finding | Correction |
| --- | --- | --- |
| Store selection | `FillList` restored sell selection and then unconditionally selected a buy row, clearing it again. | Restore the active pane and matching displayed offer; exhausted sell rows fall back to their nearest sell neighbour. |
| Buy selection | Selection fetched fresh `GetAvailableItems` results instead of selecting the actual displayed offer. | Select from the cached displayed list, retaining currency metadata and duplicate-offer identity. |
| Amount dialog | Buy required `stack == 1`; sell accepted only combined rows and was capped at a native stack. | Count whole configured trade lots, preview delivered quantity and total price, and bound by actual inventory/currency/trader funds. |
| Dialog lifecycle | Cloning retained inventory-split listeners; double-click state was shared between different rows and panes. | Remove inherited callbacks, identify a double-click by offer and pane, and close/revalidate the captured offer before committing. |
| Inventory | Partial native additions could leave unpaid items or lose part of a buyback. | Preflight exact quality/world-level capacity, snapshot original references, and roll back unsuccessful local mutations. |
| Sale sources | Shared-name removal could consume a different prefab or a hidden/equipped stack. | Remove only the exact eligible source references; use exact prefab and quality for grouping and currency counting. |
| Bulk buyback | Saving one representative item duplicated its metadata and discarded the metadata of other sold stacks. | Persist every removed stack and its original amount; preserve missing-prefab receipts until their mod is available again. |
| Arithmetic | Multiplication and balance addition could overflow, and amount preview used inconsistent rounding. | Widen arithmetic, reject unrepresentable quantities/prices, and round the whole sale quote exactly once. |
| Large sale payout | A very high configured payout could instantiate an unbounded number of world objects. | Reject/roll back a sale that would spill more than 100 native currency stacks; smaller sales or more inventory space remain possible. |
| Tooltip identity | Different prefabs sharing one localization key could inherit each other's sell prices. | Index prices by prefab; resolve prefab-owned template data without a shared-name fallback. |
| File watcher | The watcher subscribed only to legacy prefixed names, missing files the editor now creates. | Watch recognized short names inside the dedicated config directory and preserve legacy locations; handle both rename paths. |
| Reload | An unreadable or malformed file could replace working configuration with a partial snapshot. | Parse the complete candidate snapshot before publishing it and stage runtime item maps before replacing live maps. Empty files retain their existing supported meaning. |
| YAML | Trader export passed `JObject` implementation objects to YamlDotNet. | Serialize a recursively converted plain CLR object graph, retaining unknown fields and object-valued positions. |
| CSV | Duplicate headers, extra values and malformed quoted fields could silently change row meaning. | Reject those ambiguous inputs; retain quoted commas, escaped quotes, BOMs and multiline fields. |
| Editor validation | Invalid numeric edits were not dirty, and non-finite floats/positions could reach runtime. | Track edits even while invalid; reject non-finite personal values and avoid replacing valid fallback data. |
| Editor GUI | An exception could leave global `GUI.enabled` altered; Unity's intentional exit exception was swallowed. | Restore the previous enabled state in `finally` and let `ExitGUIException` propagate. |
| Remote authorization | Routed sender IDs came from client-controlled packet fields. A peer lookup by that ID was not authenticated authorization. | Use direct connection RPCs; resolve the actual `ZNetPeer` by `ZRpc`, then check that connection's host against the current server admin list. |
| Remote lifecycle | Access timeout could leave `Checking` forever; late responses and reconnects were not correlated. | Correlate requests, scope them to the actual server connection, cancel pending state on timeout/close/session changes, and ignore stale responses. |
| Remote transfer | Whole large files were sent in one RPC, and malformed input could be decoded before authorization. | Authorize before decoding file requests, bound packages to 8 MiB, fragment large transfers into ordered 32 KiB chunks, expire incomplete buffers, and validate response identity. |
| File persistence | Direct writes could truncate the original on failure; create could overwrite a concurrent file. | Stage and flush a same-directory temporary file, atomically replace an existing file, and use fail-if-existing moves for create. Reject symlink file targets. |
| Packaging | Thunderstore depended on YamlDotNet 16 while compiling against 18.1; Nexus repack overwrote compiler output. | Internalize the referenced YAML parser in both distributions, retain external CCS, and write Nexus output to a separate staging directory. |
| Publishing | Manual scripts could upload stale archives without a fresh build. | Write a source/artifact hash receipt only after both ZIP targets succeed; check it before reading secrets or contacting publishing APIs. |

## Additional findings during recovery review

- Retain the active sell pane even when its last row disappears, so a later refresh cannot steal focus back to Buy.
- Normalize rounded percentage factors before decimal price arithmetic. Values such as `0.99f` must not turn a
  99-coin quote into 100 coins; buy-floor, sell-ceiling, quality scaling, and amount limits use consistent rules.
- Buyback capacity and placement now compare every persisted per-item property (including custom data, durability,
  variant and crafter) and use explicit target slots. Native shared-name stacking must not destroy receipt metadata.
- Nested IMGUI controls inherit the disabled state of modal windows. Documents are read-only while a request is
  pending, and switching server/session makes the old document read-only until explicitly discarded or refreshed.
  A list refresh no longer silently discards a dirty document whose backing file disappeared.
- Editor string arrays retain raw string values rather than embedded JSON quotation marks; object and vector
  conversion follow runtime parsing rules. Non-finite UI scale values fall back to a usable scale.
- Periodic transfer cleanup releases abandoned buffers without waiting for another chunk. File reads enforce the
  byte limit during reading, and write/delete responses distinguish persistence success from reload failure.
- Coin weight and stack overrides apply to exact coin prefab data, update live inventory weight, and restore their
  original values on disable/unload without overwriting a subsequent modification by another mod.
- `PublishAll.cmd` reads the actual continuation answer using delayed expansion. The freshness receipt also covers
  the shipped icon and project property/solution files.

## Codex follow-up review — 2026-09-08

- Purchase capacity is evaluated against the projected inventory after its own currency-removal plan. A full inventory therefore no longer rejects a valid purchase when payment consumes a currency stack and frees the required slot. Partial payments do not create phantom slots, and same-item currency removal contributes only the resulting stack space.
- Buy-offer restoration identity now includes `m_price`, preventing two otherwise identical configured offers with different prices from being silently interchanged after a list refresh.

## Validation

No mod build, game run, C# regression execution, publishing operation, or runtime test was performed during this task.
Source-only checks are run separately: `git diff --check`, JSON/XML/project inclusion and English-text invariants.
A source parser check reports syntax only; it does not establish game-linked type correctness or Harmony compatibility.

### Retained regression assets (not executed)


`tests/Regression` compiles actual production arithmetic, selection, inventory, CSV, persistence, transfer-buffer and
editor-transport files against deterministic boundary doubles. It is designed to exercise whole lots, ceilings and overflow,
capacity and rollback, exact source identity, distinct item metadata, fractional percentage boundaries, CSV edge cases, YAML plain-data roundtrips,
atomic writes, admin revocation, path traversal, symlinks, access retry, stale responses, fragmented writes, and
connecting-client isolation. When explicitly run by the maintainer, the same runner also parses repository C# files with Roslyn in C# 11 mode.

`tests/check_repository.py` verifies compile inclusion, XML, manifest JSON, complete personal-config examples, retained
README links, English source content, and external CCS references. These checks are not a Unity runtime test.

## Remaining limitations / explicit release gate

- A complete game-linked Release build and in-game smoke test are still required. The unit doubles do not execute Unity,
  BepInEx, real Harmony chains, transport backpressure, native `Inventory.Save/Load`, or actual equipment integrations.
- Inventory rollback restores this inventory's original item references, stack counts and positions. It cannot undo
  arbitrary external effects emitted by another mod's inventory hooks. Such integrations require an in-game test.
  Fresh purchases still use native shared-name stacking; strict metadata-aware placement applies to saved buyback items.
- Trader balances retain the existing client/ZDO ownership model. This change is not an authoritative server-side
  transaction protocol and does not solve simultaneous trades by different clients or malicious gameplay clients.
- Two authorized administrators can still overwrite each other's edits: writes are atomic, but there is no optimistic
  revision/ETag contract. Remote editor changes require matching updated client and server builds; old routed handlers
  are deliberately not retained as an authorization fallback.
- A timed-out/closed editor cannot cancel a file operation already accepted by the server. Refresh before retrying a
  create/delete whose response was lost.
- Manual publishing relies on the external sibling `API/CommonPublish.ps1`. Upload APIs, credentials, and actual uploads
  were not exercised. The build receipt prevents accidental stale uploads, not deliberate tampering.

### In-game smoke cases

1. Repeated mouse/gamepad Sell with buyback enabled and disabled; last remaining sale; unchanged scroll and selection.
2. Double-click two different rows/panes versus the same row. Open and cancel the dialog without splitting inventory.
3. Buy three lots of bait/ammunition; sell two configured lots with a partial remainder; use alternate currencies.
4. Full inventory with compatible stack space, wrong-quality/world stacks, insufficient payment, exact trader balance.
5. Bulk sale then buyback, logout/rejoin, expiry, missing mod prefab, two items with different custom/enchanted data.
6. Dedicated server: administrator read/create/save/delete; guest denial; revoke admin rights; timeout then Refresh;
   reconnect to a different server; upload/download a file larger than one RPC chunk.
7. Create/edit/rename/delete a short-name JSON/YAML/CSV file outside the editor; malformed file retains the last working set.
8. Save a personal trader config as YAML, reload, and check vector position, booleans, currency and unknown fields.
9. Switch configuration targets with a dirty document, save while trying to type, and use background controls while
   a confirmation/picker is open; verify no edits leak through and no previous-server document is saved to a new target.
10. Toggle coin overrides on/off and change weight while coins are in inventory; verify weight and original values restore.
11. Clean Release build; verify both packaged DLLs contain the editor, refer to external CCS and no longer reference an
   external YamlDotNet assembly. Change a source or ZIP afterwards and verify both publish scripts refuse it.
