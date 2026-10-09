# Atlas to Apex Feature Audit

This is a technical comparison of the supplied Atlas Playbook configuration and scripts against the Apex scripts, Toolbox action catalog, and AME playbook. Atlas files remain reference-only and are not included in the Apex payload.

| Area | Atlas reference behavior | Apex status | Apex decision |
| --- | --- | --- | --- |
| Consumer AppX removal | Removes a broad set of inbox apps and can remove Store/Xbox packages. | PARTIALLY IMPLEMENTED | Apex exposes an allowlist of current-user consumer apps with snapshots and restore. Store removal is separate and warns about consequences. Protected runtimes/services are not targeted. |
| Service startup changes | Changes multiple Windows service start types, including diagnostic, sync, networking, and GPU-related services. | INTENTIONALLY EXCLUDED | Broad static service lists are build- and device-sensitive and can break updates, Defender, networking, Bluetooth, audio, GPU support, and recovery. Apex leaves services unchanged. |
| Scheduled task reduction | Disables selected Application Experience, AppX deployment, and usage-reporting tasks. | INTENTIONALLY EXCLUDED | There is no safe universal task allowlist for supported Windows builds; Apex does not disable scheduled tasks without per-build evidence and restore tests. |
| Privacy registry changes | Changes input, Office, NVIDIA, .NET CLI, and Windows diagnostic preferences. | PARTIALLY IMPLEMENTED | Apex currently offers reversible current-user Advertising ID and tailored diagnostic-personalization settings. It does not claim to disable OS telemetry or third-party product telemetry. |
| Telemetry services | Disables or alters telemetry-related services and components. | INTENTIONALLY EXCLUDED | No machine-wide telemetry service changes are made because their dependencies and policy behavior vary by Windows build and edition. |
| Windows consumer experiences | Removes or alters promoted apps, suggestions, and consumer experiences. | PARTIALLY IMPLEMENTED | Apex provides current-user removal for a bounded consumer-app allowlist. Advertising ID/personalization is reversible. No broad policy bundle is applied. |
| Windows features/capabilities | Adds/removes selected optional features and capabilities. | MISSING | No feature/capability removal is added: available names, dependencies, and reversibility vary by Windows edition/build. A future per-feature allowlist needs clean-VM install and restore evidence first. |
| Startup items | Changes startup behavior and taskbar/browser startup choices. | PARTIALLY IMPLEMENTED | Apex inventories current-user Run registry entries and can disable a selected entry after confirmation, saving its exact value/type for restore. Other users, Startup folders, services, and scheduled tasks remain untouched; Windows Startup Apps remains available. |
| Background apps | Applies broad background-activity policies. | PARTIALLY IMPLEMENTED | RAM Saver has reversible Balanced/Aggressive user policy; Background Activity exposes startup/background settings. It does not kill processes or disable services. |
| Search/indexing | Applies reduced indexing profiles and search-related changes. | PARTIALLY IMPLEMENTED | Apex opens Search and Indexing Options and leaves Windows Search running. Per-location indexing profiles are not implemented. |
| Visual effects and animations | Applies performance-oriented UI effects changes. | IMPLEMENTED | Apex has a reversible visual-effects script and theme-aware Toolbox feedback. Windows UI effects are verified from current-user state. |
| Gaming/GameDVR | Changes Game Mode, Game Bar, and capture preferences. | PARTIALLY IMPLEMENTED | Apex reversibly enables Game Mode and disables Game DVR capture values for the current user, and provides direct Game Mode/Game Bar settings links. It does not change machine policy or remove gaming components. |
| Fullscreen/game compatibility | Applies broad game-related compatibility changes. | INTENTIONALLY EXCLUDED | Global fullscreen/GPU/driver tweaks are title- and driver-specific. Apex provides per-app Graphics Settings and does not guess per-game settings. |
| Power plans | Selects a built-in plan and changes processor values. | IMPLEMENTED | Apex enumerates installed schemes and can activate an exact installed scheme by its live GUID, verifying the actual active plan and preserving the previously active GUID for restore. Optional Apex profiles clone available Windows plans and verify supported processor settings; no fixed scheme GUID is assumed. |
| Network stack | Changes network defaults and can reset networking. | PARTIALLY IMPLEMENTED | Apex diagnoses adapters/gateway/TCP/DNS; safe repair flushes DNS only when indicated. Winsock/TCP-IP resets are separate Advanced, confirmed actions with a before-state backup and restart warning. |
| Explorer | Applies context-menu and Explorer shell changes. | PARTIALLY IMPLEMENTED | Apex supports reversible Windows 11/classic context-menu choices and Explorer restart. Other Atlas folder-discovery/tuning options are not copied. |
| Browser selection/install | Offers browser choices and installs selected browsers. | IMPLEMENTED | Apex first-run setup offers Chrome, Firefox, Brave, or Keep Existing. It only invokes the chosen exact WinGet package ID, does not silently change defaults, and checks local install presence afterward. Windows installation verification remains pending. |
| Optional software | Installs utilities and other selected packages. | IMPLEMENTED | Apex offers one optional game launcher, OBS, and NanaZip/7-Zip in first-run setup; the Software page permits individual installs. WinGet is only invoked after explicit selection. |
| Security/Defender | Atlas contains Defender-related changes, including disable paths. | INTENTIONALLY EXCLUDED | Apex never disables Defender, Firewall, Security Center, or Windows Update security mechanisms. It reads Defender/Firewall status and links to Windows Security. |
| Xbox/Gaming Services | Removes/changes some Xbox-related packages and dependencies. | INTENTIONALLY EXCLUDED | Apex preserves Xbox identity, Gaming Services, and Minecraft dependencies; only read-only diagnostics and optional Minecraft Launcher installation are provided. |
| Windows Update | Atlas changes update behavior/services/tasks. | INTENTIONALLY EXCLUDED | Apex does not disable update policy or update services. It provides Windows Update diagnostics/repair and a Settings link. |
| Compatibility | Atlas uses build-specific assumptions and package conditions. | PARTIALLY IMPLEMENTED | Apex reports Windows release/build and detects Edge/WebView2 and related compatibility components. No broad multi-build VM matrix has been run here. |
| Repair/recovery | Includes backup and reset/restore workflows. | IMPLEMENTED | Apex includes restore points, configuration backup, reversible registry snapshots, DISM/SFC, Windows Update cache repair, WinRE diagnostics, and action logs. These are not a full system image. |
| Lock-screen images | Uses Windows Runtime `LockScreen.SetImageFileAsync`. | PARTIALLY IMPLEMENTED | Apex uses that API, checks support and an active stream, and logs Windows rejection. The API does not expose the active image's source path, so exact-file identity is not claimed; real Windows verification is still required. |

## Additional Atlas Configuration Crosswalk

This supplements the table above with the concrete configuration families found in `Configuration/atlas` and `Configuration/tweaks`. Status describes useful technical behavior, not similarity of UI or naming.

| Atlas functionality | Apex status | Add/adapt decision |
| --- | --- | --- |
| AppX allowlist removal for consumer apps | PARTIALLY IMPLEMENTED | Apex offers a narrower current-user allowlist with per-action confirmation and best-effort restore. It does not remove apps for every user or block future provisioning. |
| AppX deprovisioning, package reinstall blocking, and cache purges | INTENTIONALLY EXCLUDED | Machine-wide deprovisioning and cache clearing can affect other accounts, upgrades, Store repair, Start/Search, and security UI. |
| Edge, OneDrive, Security Center, Defender, and telemetry component removal | INTENTIONALLY EXCLUDED | Edge/WebView2, Defender, Security Center, OneDrive integration, and security/update components are not removed or disabled. Defender and Firewall status are read-only. |
| Service and driver startup-type changes, including diagnostics, networking, and GPU-adjacent services | INTENTIONALLY EXCLUDED | No universal service allowlist is safe across Windows editions, hardware, VPNs, accessibility, and managed devices. Apex leaves service startup and drivers unchanged. |
| Application Experience, AppX deployment, Disk Diagnostic, CEIP, and usage-reporting scheduled tasks | INTENTIONALLY EXCLUDED | Apex does not disable scheduled tasks without build-specific dependency analysis, prior-state backups, and Windows Update/recovery regression testing. |
| Delivery Optimization, Storage Sense, and reserved-storage policy | MISSING | Storage Settings is a review shortcut only; Apex does not directly alter delivery, cleanup, or reserved-storage policy. Destructive cleanup is intentionally not inferred or run automatically. |
| Background Store-app activity | PARTIALLY IMPLEMENTED | RAM Saver changes the current-user Windows background-app policy with captured prior state; it does not terminate processes or disable services. |
| Minimal Search indexing and web/search policy | PARTIALLY IMPLEMENTED | Apex opens Windows Search and Indexing Options; it does not change index locations, disable Windows Search, or apply machine policy. |
| Multimedia scheduling, system responsiveness, Win32 priority separation, and fault-tolerant heap settings | INTENTIONALLY EXCLUDED | These global scheduler/heap changes have workload- and build-dependent effects and lack a reliable universal performance benefit. |
| Paging-file disablement, service-host split, and NTFS low-level optimization | INTENTIONALLY EXCLUDED | Disabling paging or changing service-host/filesystem internals can cause instability or compatibility failures; Apex preserves Windows-managed paging and defaults. |
| LLMNR, SMB bandwidth, anonymous-share restrictions, and file-sharing defaults | INTENTIONALLY EXCLUDED | These are security/network policy changes, not generic performance tweaks. Apex does not silently change sharing or name-resolution policy. |
| Per-user advertising ID and diagnostic personalization | PARTIALLY IMPLEMENTED | Apex provides reversible controls for these two preferences only. It does not claim to disable OS telemetry. |
| Activity history, speech, sync, app permissions, experimentation, error reporting, Office/NVIDIA telemetry, and other Atlas privacy policies | MISSING | No broad copied registry bundle is applied. A reversible per-setting inventory remains future work after compatibility testing. |
| Global fullscreen, BCD/boot, timer-resolution, interrupt-affinity, and driver-affinity tweaks | INTENTIONALLY EXCLUDED | These are title-, hardware-, firmware-, and workload-sensitive; global application can harm latency, battery, device stability, or recovery. Apex uses per-app Graphics settings and does not guess. |
| Explorer folder discovery, navigation-pane, recent-items, shortcut, and context-menu changes | PARTIALLY IMPLEMENTED | Apex supports reversible context-menu mode and Explorer restart. Remaining shell preferences are not broadly changed because they are mostly preference choices rather than reliable performance wins. |
| Windows Update deferrals, notifications, or service/task changes | INTENTIONALLY EXCLUDED | Apex preserves Windows Update and offers settings, diagnostics, and cache repair without policy deferral or disabling update infrastructure. |
| Safe Mode boot scripts and Reset this PC links | PARTIALLY IMPLEMENTED | Apex provides WinRE diagnostics and repair/recovery entry points, but does not edit BCD or automate destructive reset workflows. |
| Third-party optional software installation | IMPLEMENTED | Apex installs only individually selected catalog items through exact WinGet IDs and verifies local installation; browser installation does not silently change default associations. |
| Atlas theme, logos, URLs, application identity, and Atlas-specific assumptions | INTENTIONALLY EXCLUDED | Apex uses its own name, icons, assets, scripts, and AME playbook identity. |

## Source Areas Reviewed

- `AtlasOs Source/AtlasPlaybook_v0.5.0/Configuration/atlas/{appx,components,default,revert,services,start}.yml`
- `AtlasOs Source/AtlasPlaybook_v0.5.0/Configuration/tweaks.yml` and the `tweaks/{debloat,networking,performance,privacy,qol,security}` groups
- Atlas `Modules/{Debloat,Performance,Privacy,Qol,Scripts,Themes}` and browser/software setup scripts
- Apex `config/toolbox.json`, `scripts/`, `playbook-source/`, and `src/ApexToolbox/`

## Intentionally Not Ported

- Bulk machine-wide service/task disabling without per-build dependency tests and verified rollback.
- Defender, Firewall, Edge/WebView2, Xbox/Gaming Services, or Windows Update removal/disable operations.
- Fixed power GUIDs, generic telemetry bundles, or undocumented registry optimizations.
- Atlas branding, names, URLs, assets, package assumptions, and Atlas UI structure.

## Verification Boundary

This audit is source-based. In this Linux container, project validation, AME source validation, and a `net8.0-windows` compile pass; PowerShell parsing is unavailable because neither PowerShell nor `pwsh` is installed. Windows effects, WinGet package identity/availability, Defender/Firewall query results, startup registry rollback, network resets, powercfg behavior, desktop wallpaper, lock screen, and AME import/install must still be tested on a disposable Windows 11 system. None are represented here as Windows-runtime verified.