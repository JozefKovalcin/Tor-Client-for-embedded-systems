# Windows product backlog

This repository copy maps the supplied canonical backlog to separately reviewable Windows work. Its original checklist is preserved below.

| ID | Sekcia | Popis | Stav | Dôkaz | PR/commit | Blocker | Ďalšia akcia |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WIN-00 | 0 | Current GitHub head and offline baseline | VERIFIED | GitHub main matched `68002b9`, 174 portable PASS, 10 platform skips, 6 pipeline PASS | #29 and draft PR #30 | — | Rebase a later batch on the latest reviewed main |
| WIN-01 | 1 | Persistent TLS identity, contact pairing, endpoint editing and remote pin replacement | VERIFIED | DPAPI fixture and Windows presentation tests | #29 and draft PR #30 | — | Add explicit local credential recovery and replacement |
| WIN-02 | 2 | Durable outbox, authenticated ACK, history and deduplication | VERIFIED | Loopback TLS v2, lost ACK, replay, reopened stores, TTL and opt-in history | #29 and draft PR #30 | — | Add per-message sent/retrying state and complete storage failure coverage |
| WIN-03 | 3 | Peers page, status and reconnection | PARTIAL | 23 Windows peer assertions. Public same-address restore and publication passed. The exchange after restart failed. [Single-host evidence](../reports/windows-persistent-host-2026-11-18/README.md). | #30–#32 | Public reconnect FAIL. DPI matrix NOT RUN. | Repeat the public scenario with stage diagnostics after approval and check the DPI matrix. |
| WIN-2PC | 3 | Qualify the current GUI on two physical PCs | NOT RUN | Operator runbook | #29 and draft PR #30 | Separate approval and second PC | Execute after software gates |
| WIN-04 | 4 | Dependency Manager | PARTIAL | Real read-only scan, pinned plan, signature gate, rescan and historical local transaction | #29 | Real install requires consent and an eligible candidate | Verify current GUI install/repair end to end with approved inputs |
| WIN-05 | 5 | Windows Native Build Manager | VERIFIED for optimized-static | Real GUI build exit 0, PE closure, eight offline smoke checks and verified ZIP export, plus activation and rollback fixture | Draft #32 | Shared build requires separate consent | Qualify optimized-shared and the approved public suite |
| WIN-06 | 6 | Versioned self-contained peer distribution | VERIFIED | Current locked win-x64 ZIP, bundled Tor, hash inventory, native imports and real development-host offline smoke | Draft #32 | Clean Windows host for field qualification | Test the same package on a clean compatible host |
| WIN-07 | 7 and 8 | Reliability, storage faults and soak | PARTIAL | Reproduced Windows journal rename failure, verified bounded retry, native offline lifecycle and loopback recovery | #29 and draft PR #30 | Public endurance requires separate approval | Extend offline fault coverage and record comparable soak metrics |

---
# TOR CLIENT / WINDOWS TOR MANAGER — AUTONÓMNY MASTER BACKLOG PRE CODEX

**Dátum východiskového stavu:** 18. 11. 2026
**Repozitár:** `JozefKovalcin/Tor-Client-for-embedded-systems`
**Hlavný cieľ:** stabilná, autentifikovaná a obojsmerná komunikácia medzi **dvoma samostatnými Windows PC cez verejnú sieť Tor**, potom použiteľné Windows GUI, správa závislostí, vlastná kompilácia a finálna distribúcia. **Embedded je výslovne odložený.**

> **Pozor:** Počiatočné `[x]` nižšie sú PREVZATÉ Z POUŽÍVATEĽOVHO CHECKLISTU (historicky implementované alebo testované). Nie sú automatickým novým overením posledného HEAD. Codex má každú položku rozlíšiť na `IMPLEMENTED`, `VERIFIED`, `PARTIAL`, `NOT RUN`, `BLOCKED`, `FAILED`; zaškrtnutie môže zmeniť len po dôkaze. Nemaž záznamy o neúspešných testoch.

## Pravidlá práce pre autonómneho agenta

1. Prečítaj `AGENTS.md`, `docs/HANDOFF.md`, `docs/PROGRESS.md`, `docs/ROADMAP.md`, relevantné `docs/architecture/`, pôvodné reporty TASK 01–11/12 a existujúce testy. Over aktuálny GitHub `main`, draft PR, worktrees, stashe, tag `baseline` a lokálne nezapísané zmeny. **Nespoliehaj sa na zastarané SHA v tomto súbore.**
2. Pri tvorbe pôvodného plánu bol `main` `266e29a29cab4b465415d8388614493a661e77f8`. PR #29 následne integroval hlavy #19–#28 do `main` na `68002b9b4a31e5da5acc152a3c17b3e49da7daed`. Pri ďalšom cykle znovu over aktuálny GitHub a regresie. Merge vyžaduje samostatný súhlas.
3. Založ alebo aktualizuj v repozitári `docs/WINDOWS_PRODUCT_BACKLOG.md` podľa nižšie uvedených položiek a `docs/AUTONOMOUS_PROGRESS.md` so stavmi, ownerom, PR, dôkazmi, testami a blokermi. Nikdy nepovažuj samotný green CI za dôkaz fyzického testu na dvoch PC alebo clean-host kvalifikácie.
4. Pracuj v **malých inkrementoch**. Pre každý inkrement: analýza → konkrétne acceptance criteria → regresný test (pokiaľ možno reprodukcia pred opravou) → implementácia → lokálne kontroly → samostatný draft PR → CI → aktualizácia backlogu. Nezlučuj automaticky. Nepoužívaj force push, destructive rebase, stash drop ani clean/reset cudzej práce.
5. Pokračuj po jednom dokončenom balíku na ďalší nezávislý balík **bez opakovaného pýtania sa na bežné implementačné detaily**. Ak jeden balík narazí na externý blocker, zaznamenaj ho a pokračuj ďalším nezávislým. Ak má blocker vplyv na identitu, bezpečnosť alebo kompatibilitu, neobchádzaj ho.
6. Paralelní agenti môžu pracovať iba v **izolovaných worktrees s neprerývajúcim sa vlastníctvom súborov**. Najprv zosúlaď shared interfaces; priraď explicitné vlastníctvo každému PR. Spoločné testy môže spúšťať koordinátor, nie súčasne do toho istého mutable build/output adresára.
7. Existujúci natívny Windows Tor, `windows-native/`, `COMPILE.cmd`, Windows patch, source locks, W1/W2/W3/W5/W6, Win32 job handling, Tor Control, mutual TLS a prenos správ **maximálne opätovne používaj**. Zachovaj pôvodných 9 funkčných Tor testov, historické výsledky a chránené súbory. Všetky identity, cookies, súkromné certifikáty a writable Tor state iba na endpointoch a mimo Git.
8. Nikdy neoslabuj TLS trust/pinning, Tor SOCKS remote DNS/no direct fallback, owner-only identity directory, SAFECOOKIE, loopback listener policy, kontrolu hashov, bezpečné ukončenie Tor procesov a zdokumentované limity. Autentifikácia nepriateľského peeru je samostatná od samotného spojenia cez Onion Service.
9. **Schválenie používateľa je povinné** pred: inštaláciou alebo aktualizáciou systémových nástrojov, privilégiami správcu, akýmkoľvek novým sťahovaním build/source balíkov, verejnými Tor E2E/soak testami, zmenou reálneho sieťového/proxy nastavenia, produkčným podpisom/publikovaním, mazaním/zmenou identít, firmware flash/update, drahým/masívnym buildom mimo už schválených hraníc a merge do `main`. Môžeš pripraviť plán, UI a testy so simulovanými/existujúcimi vstupmi bez tohto schválenia. Ak chýba autorizácia: `BLOCKED`, NIE `PASS`.
10. Bežné bezpečné lokálne čítanie, editácia v izolovaných worktrees, statické kontroly, build/test v predtým schválenom prostredí a vytváranie draft PR sú povolené v medziach runtime oprávnení. Nikdy nepredstieraj oprávnenia na GitHub alebo lokálny host, ktoré agent nemá.
11. Po každom PR zaznamenaj: presný HEAD SHA, diff, testy `PASS/FAIL/SKIPPED/BLOCKED/NOT RUN`, artefakty a hash, otvorené riziká, regresie vo Windows/WSL a príslušné GUI dôkazy. Tam, kde vyžaduješ fyzické 2-PC testy, vypíš presný postup pre operátora a čakaj na jeho výsledok.
12. Neimplementuj všetky oblasti naraz. Priebežne udržuj stav podľa sekcií a **nezamieňaj prítomnosť kódu s kvalifikovaným vydaním**.

## Odporúčané poradie autonómnej realizácie

| Etapa | Priorita | Obsah | Exit gate / blokery |
|---|---|---|---|
| 0 — Freeze/Review | P0 | Overiť aktuálny `main`, PR #19, regresie, zjednotiť master backlog a integračné rozhrania | Review PR #19; bez automatického merge; preukázať kompatibilitu |
| 1 — Peer reliability & trust | P0 | Trvalé identity, párovanie, kontaktový model, reconnect/state, identitné chyby | Bez tichej regenerácie alebo zmeny dôvery; testy pred/po reštarte |
| 2 — Messages & local history | P0/P1 | Status správ, fronta počas výpadku, idempotencia/replay, opt-in šifrovaná história | Deterministické testy; bezpečné lokálne ukladanie; nenárokovať exactly-once bez potvrdení |
| 3 — Windows GUI peer UX | P1 | Peers/Connections, Onion services, dôkazy dvoch PC, accessible UI | Skutočné vizuálne testy a operátorom vykonaný 2-PC test |
| 4 — Dependencies | P1 | Napojenie W2, reálny scan, install planning, schvaľovaný executor | Inštalácie až po konkrétnom schválení, Run Only bez SDK/MSYS2 |
| 5 — Native Tor Build Manager | P1 | Preflight, existujúci COMPILE.cmd/native pipeline, logy, cancel, profily, validácia, activation/rollback | Real build až v schválenom prostredí, izolované workspace, bezpečné prepnutie runtime |
| 6 — Release qualification | P1 | Peer-enabled self-contained ZIP, clean Windows, neadmin, integrity, usability | Skutočný clean-host a dvoj-PC acceptance, inak `NOT RUN` |
| 7 — Security/stress/long soak | P1 | Threat model, failure matrix, soak, perf, lost connectivity | Verejné/soak testy len po súhlase, presné namerané metriky |
| 8 — Embedded | DEFERRED | Linux/ARM/PICO/Yocto/NAND/USB gadget | **Nespúšťať**, kým používateľ explicitne nezmení prioritu |

Etapy 1–3 sú kritické pre primárny cieľ A ↔ B; etapy 4–6 dopĺňajú pôvodnú víziu Windows Managera. Časti s nezávislými súbormi môžu ísť paralelne, nikdy však nie na úkor peer funkčnosti.

## Konkrétne kontrakty / akceptačné kritériá

### Peer a identity
- Rozlíš **host onion identity**, **TLS identitu klienta** a **lokálny trust store**. Definuj ich vlastníctvo, vytvorenie, obnovu/zálohu, rotáciu a revokáciu.
- Pairing vyžaduje dôveryhodný out-of-band kanál na porovnanie fingerprintov alebo ekvivalentný autentifikovaný mechanizmus. Nepovažuj unverified TOFU za automaticky dôveryhodný.
- Peer zmena kľúča = viditeľná udalosť a schválenie, nie automatické prepnutie. Dôveryhodnú identitu nikdy neprepíš bez auditovanej akcie.
- Definuj vzťah reconnectu a opakovaného doručenia. Napr. message ID + sequence + explicit ACK + trvalé deduplikačné okno, s dokumentovanou sémantikou at-least-once/at-most-once. Neprisľubuj exactly-once bez distribuovaného dôkazu.
- Definuj limity fronty (počet, bajty, TTL), perzistenciu, backpressure, recovery a ochranu súkromia. Žiadne neobmedzené RAM/fronty.

### GUI/Developer
- `ConnectionMode = Demo | RealOffline | RealOnline`, `WorkspaceMode = RunOnly | Developer | Embedded` a `RuntimeSource = Bundled | CustomVerified` majú byť **oddelené osi**, nie vzájomne si prekážajúce režimy. Embedded UI môže byť iba Disabled/Deferred.
- Dátové modely W1 a W2 reuse; nič simulované nezobraziť ako reálne. Build progress percentá len pri merateľnom progrese; inak indeterminate.
- `Run Only` funguje bez vývojových závislostí; `Developer` odhaľuje skutočný sken a compilation pipeline; `Embedded` ostáva neaktívny do autorizácie projektu.

### Dependency Manager
- Skener nesmie inštalovať nič; klasifikácie a verzie viaž na presné toolchain požiadavky z existujúcej Windows recipe.
- Inštalačný plán obsahuje názov, požadovanú/povolenú verziu, zdroj, hash alebo trusted signature, licenciu, scope, privilegium, PATH/reboot dopady, disk a riziko.
- Installer vykonáva iba explicitne schválený allowlist, nikdy ľubovoľný shell od UI/metadata; znovu skenuj po inštalácii.

### Native Windows Build Manager
- Wrapper reuse existujúci `COMPILE.cmd`/`windows-native` postup, s predbežným *read-only* preflightom a oddelenými súhlasmi na sťahovanie/inštaláciu/kompiláciu.
- Build input hash manifest, isolated output, process-tree cancel, logy s limitmi, reálne exit kódy, PE/DLL closure, pinned version, license a regression smoke.
- Aktivácia vlastného build je atómová voči konfigurácii novej relácie; aktívny Tor nikdy nevymieňaj. Zachovaj posledný verified runtime a možnosť rollbacku.

### Release qualification
- Celý **aktuálny** peer build v portable self-contained ZIP + Tor binary/DLL, nie starší GUI-only preview. Windows bez systémového .NET a bez MSYS2, bežný účet, read-only install directory, paths with spaces, fresh state.
- Nezabudni reálne GUI end-to-end na dvoch fyzických PC a negatívne testy integrity, identity a crash recovery. Publikovanie/update mechanizmus je samostatný security gate.

---

# HLAVNÝ CHECKLIST (pôvodné používateľské znenie)

## 1. Komunikácia cez Tor – hlavný cieľ
- [x] Obaja klienti dosiahnu bootstrap Tor 100 %
- [x] Publikovanie Onion Service v3
- [x] Pripojenie na .onion adresu cez SOCKS5
- [x] Obojsmerné odosielanie a prijímanie správ
- [x] Vzájomné TLS overenie peerov
- [x] Kontrola integrity prenesených správ
- [x] Opätovné pripojenie po reštarte Toru
- [x] Úspešné spojenie na dvoch samostatných Windows PC
- [ ] Presné zopakovanie dvoj-PC testu cez finálne GUI a archivácia dôkazov
- [ ] Dlhodobé spojenie a opakované reconnecty
- [x] Automatické obnovenie aktívne hostovanej adresy po reštarte Toru (offline fixture)
- [x] Fronta správ počas krátkodobého výpadku
- [x] Trvalé aplikáčné identity a dôveryhodné párovanie klientov
- [ ] Bezpečné obnovenie relácie bez tichej zmeny identit

## 2. Správy a prenos súborov
- [x] Verziovaný protokol a rámcovanie správ
- [x] Obmedzenie veľkosti rámcov a kontrola poškodenia
- [x] Súbežné odosielanie a prijímanie
- [x] Offline testy chybných správ a autentifikácie
- [x] História konverzácie v GUI
- [ ] Stavy odoslané / doručené / zlyhané
- [x] Uloženie konverzácie, ak si ho používateľ zapne

## 3. Windows GUI – používanie
- [x] Avalonia desktop sa zostaví a spustí
- [x] Real Offline režim
- [x] Real Online režim
- [x] Demo režim
- [x] Start / Stop Tor
- [x] Zobrazenie skutočného bootstrapu
- [x] SAFECOOKIE stav a živé sanované logy
- [x] Restart, New Identity a SOCKS5 test – implementované ovládanie
- [x] Peer messaging panel v PR #19
- [x] Skutočné Peers UI so správou kontaktov
- [ ] Kompletné vytváranie a úprava Onion Services cez GUI
- [x] Persistentné uloženie peer profilov a ich identity
- [ ] Prehľadný stav spojenia oboch peerov a histórie chýb
- [x] Oddelenie Connection mode / Workspace mode / Runtime source (výber runtime platí pri nasledujúcom štarte)
- [x] Developer stránky funkčné aj mimo Demo režimu
- [x] Skutočný Build Manager priamo v GUI (kompilácia vyžaduje samostatný akceptačný beh)
- [x] Skutočný Dependency Manager priamo v GUI
- [ ] Ovládanie zdrojových buildov a vlastného Tor runtime
- [ ] Prístupnosť, škálovanie DPI a dôkladná vizuálna kontrola

## 4. Dependency Manager – zabudnutá pôvodná funkcia
- [x] W2 read-only diagnostický základ
- [x] Klasifikácia dostupnosti a kompatibility nástrojov
- [x] Napojenie diagnostiky na reálne údaje v GUI
- [x] Rozlíšenie runtime / build / test závislostí
- [x] Zobrazenie zistenej verzie, cesty a požadovanej verzie
- [x] Detekcia Windows architektúry, voľnej RAM a diskovej kapacity
- [ ] Detekcia .NET SDK/runtime a balíkov
- [ ] Detekcia MSYS2 UCRT64 a správneho toolchainu
- [ ] Detekcia GCC, Make, Autotools, Perl, Python a ostatných skutočne potrebných nástrojov
- [ ] Detekcia knižníc, hlavičiek a zdrojových archívov
- [ ] Stav Installed / Missing / Incompatible / Unknown / Blocked
- [ ] Zobrazenie, čo Manager môže doinštalovať sám
- [x] Presný inštalačný plán pred potvrdením
- [ ] Inštalácia vybraných chýbajúcich nástrojov po súhlase
- [ ] Overenie pôvodu, podpisu a hashu stiahnutých balíkov
- [ ] Re-scan po dokončení inštalácie
- [ ] Retry a bezpečné riešenie neúspešnej inštalácie
- [x] Režim bez automatických sťahovaní (offline)
- [x] Samostatné oprávnenia a potvrdenie pri zásahu do systému

## 5. Windows Build Manager – vlastná kompilácia
- [x] Existujúci natívny Windows source-build pipeline
- [x] Samostatný COMPILE.cmd balík
- [x] Kompilácia Toru cez MSYS2 UCRT64 / MinGW-w64
- [x] Optimalizovaný static build
- [x] Shared build profil
- [x] Hash-locked zdroje a Windows patch
- [x] Overenie výsledných PE/DLL súborov v pôvodnom pipeline
- [x] Prepojenie existujúceho build pipeline na GUI (overenie natívnej kompilácie je v sekcii 8)
- [x] Výber profilu buildovania v GUI
- [x] Výber priečinka zdrojových archívov a výstupného workspace
- [x] Read-only preflight pred kompiláciou
- [x] Tlačidlo Compile verified sources
- [ ] Reálny progress, fázy a živé build logy
- [ ] Cancel a cleanup celého build process tree
- [ ] Správa build chýb a návrhy opravy
- [ ] História predchádzajúcich buildov a manifestov
- [ ] Automatické offline smoke testy po skompilovaní
- [x] Overené aktivovanie vlastného Tor build runtime s existujúcim overeným balíkom
- [x] Rollback na predchádzajúci funkčný build
- [x] Ochrana aktívnej relácie pred prepnutím binárky
- [x] Možnosť exportu zostaveného Tor runtime balíka (overená na lokálnom fixture)

## 6. Windows distribúcia a inštalácia
- [x] Získanie troch win-x64 .NET runtime balíkov
- [x] Uzamknutý restore a self-contained build
- [x] Vytvorenie skúšobného ZIP s .NET runtime
- [x] Spustenie z rozbaleného balíka
- [x] Overený pribalený Tor v offline režime
- [x] SHA-256 manifesty a kontrola balenia
- [x] Lokálny self-contained kandidát obsahujúci aktuálnu Peer funkcionalitu
- [ ] Clean Windows test bez predinštalovaného .NET
- [ ] Test na podporovaných verziách Windows a x64
- [x] Bezproblémové spustenie z priečinkov s medzerami
- [ ] Test v read-only adresári a pod neadmin účtom
- [ ] Automatická detekcia runtime vs developer závislostí
- [ ] Jednoduchý prvý štart a konfigurácia
- [ ] Kontrola integrity pri každom aktivovaní Tor runtime
- [ ] Versioned release a aktualizačný mechanizmus
- [x] Dokumentácia pre bežného používateľa

## 7. Bezpečnosť a stabilita
- [x] SAFECOOKIE autentifikácia ControlPortu
- [x] Loopback-only ControlPort
- [x] Vlastníctvo Windows Tor procesu a Job Object
- [x] Čistenie procesov pri Stop/Close
- [x] TLS peer autentifikácia s pinningom
- [x] Obmedzené rámce a sanované logy
- [x] Testy nesprávnej TLS identity a poškodených dát
- [x] Súkromný Tor DataDirectory
- [x] Ochrana pred priamym SOCKS5 fallback v testovacom klientovi
- [x] Bezpečné perzistentné párovanie peer identít
- [x] Dokumentovaný trust model a revokácia peerov
- [ ] Dlhodobý replay/duplicate protection medzi reláciami
- [x] Obnova rozpracovaných správ po reštarte v rámci TTL
- [ ] Rozsiahle záťažové a pamäťové testy
- [ ] Testy sieťových výpadkov, firewallu a nedostupného peeru
- [ ] Revízia uloženia kľúčov a ich zálohovania
- [ ] Threat model pre kompletný Windows produkt
- [ ] Bezpečnostné review každej budúcej inštalácie alebo kompilácie

## 8. Testovanie a kvalifikácia
- [x] Pôvodných 9 Tor funkčných testov
- [x] Natívne Windows offline lifecycle testy
- [x] Dvojprocesový verejný Tor test
- [x] Obojsmerný TLS peer E2E test
- [x] Úspešné spojenie na dvoch PC – potvrdené používateľom
- [x] CI a regresie Windows / WSL
- [x] Test reconnectu po reštarte Toru
- [ ] Test celej komunikácie výhradne cez finálne GUI na dvoch PC
- [ ] Reprodukovateľný dvoj-PC automatizovaný testovací protokol
- [ ] Dlhodobý soak/endurance test
- [ ] Clean-host release matrix
- [x] Test vlastnej kompilácie spustenej z GUI pre `optimized-static`
- [ ] Test auto-inštalácie závislostí na čistom Developer PC
- [ ] Test prevádzky bez admin práv
- [ ] Ucelený finálny Windows acceptance report

## 9. Linux / Embedded – odložené, ale nezabudnúť
- [x] Buildroot x86 referenčný obraz a testy
- [x] ARM QEMU build a deväť funkčných testov
- [x] RAM kvalifikácia 128 / 256 MiB v emulácii
- [x] Výber predbežného fyzického HW
- [x] W3 native Linux preflight
- [x] W6 Linux executor a ELF inventory implementované
- [ ] Skutočný natívny Linux Tor build cez W6
- [ ] Native Linux runtime/distribution kvalifikácia
- [ ] Overenie presného fyzického modulu, carrier a DTB
- [ ] UART boot a Ethernet public bootstrap na fyzickej doske
- [ ] USB gadget komunikácia medzi Windows a endpointom
- [ ] Mutual authentication/bridge cez USB
- [ ] NAND layout, persistent state a recovery
- [ ] Bezpečná aktualizácia a rollback firmware
- [ ] Dva fyzické embedded endpointy s E2E Tor komunikáciou
- [ ] Dlhodobé testy flash, výkonu a spotreby

---

## Autonómna pracovná slučka — vykonávaj, nie iba plánuj

Pre každú nedokončenú Windows etapu:

1. **Inventory:** Načítaj kód a testy, porovnaj s checklistom, nájdi čiastočné implementácie a skutočné blockers. Spíš minimálne akceptačné kritériá.
2. **Select next:** Vyber najvyššiu prioritu, ktorá je technicky realizovateľná bez cudzieho schválenia. Nezastavuj sa len preto, že iná etapa čaká na používateľa.
3. **Implement:** Založ `codex/windows-<feature>` a izolovaný worktree, implementuj malú funkčnú časť a relevantné testy. Reuse kontrakty a existujúce bezpečnostné mechanizmy.
4. **Verify:** Behaj lokálne relevantné testy, potom celú bezpečnú regresiu a CI. Otestuj reálne GUI, ak je prostredie dostupné. Nevydávaj mocks za E2E.
5. **Review:** Urob self-review + nezávislé security/correctness review; chyby oprav a zopakuj regresie.
6. **PR:** Vytvor samostatný draft PR s jasným opisom, presným HEAD, testami a blokermi. Nezlučuj do `main` bez explicitného súhlasu.
7. **Track:** Aktualizuj backlog/report a zapíš, na čo konkrétne treba fyzický PC, sieť, schválenie inštalácie alebo operátora.
8. **Continue:** Pokračuj ďalším bezpečne implementovateľným balíkom. Ak skončí dostupná session alebo runtime limit, zanechaj obnoviteľný `NEXT_ACTION` a presný stav v repozitári, nepredstieraj automatické neobmedzené pokračovanie.

### Správa postupu — predpísaný formát

Pre každú položku používaj tabuľku:

`ID | Sekcia | Popis | Stav | Dôkaz (test/GUI/host) | PR/commit | Blocker | Ďalšia akcia`

Ďalej vytvor `docs/AUTONOMOUS_PROGRESS.md` s časťami:

- `CURRENT_BASE` (SHA a relevantné PR)
- `COMPLETED_THIS_RUN`
- `TEST_EVIDENCE`
- `OPEN_PULL_REQUESTS`
- `BLOCKED_AWAITING_APPROVAL`
- `PENDING_PHYSICAL_TWO_PC_CHECKS`
- `NEXT_ACTION`

### Nepovinné rozšírenia mimo pôvodného checklistu

Tieto položky neimplementuj bez samostatného schválenia scope: automatické systémové proxy nastavenia, anonymizačné tvrdenia bez testov, výmena vlastného TLS protokolu, multiparty routing, cloudové účty, telemetria, automatické aktualizácie s privilégami, embedded firmware, šifrovaný cloud sync. Ak sa ukážu nevyhnutné, navrhni ich ako oddelenú zmenu s threat modelom.

## Míľniky Windows fázy

**DEVELOPMENT COMPLETE** vyžaduje všetky Windows funkcie, úspešný build a
dostupné automatizované akceptačné testy. Stav je PARTIAL.

**FIELD QUALIFIED** vyžaduje samostatné akceptačné dôkazy. Stav je NOT RUN.
- dva nezávislé Windows PC cez finálne GUI (nie len harness) vytvoria dôveryhodne spárované Tor spojenie, vymenia správy a zvládnu výpadok/reconnect;
- bezpečne sa zachová identita a lokálne dáta podľa explicitného nastavenia;
- Build Manager dokáže v oprávnenom Windows prostredí vlastný Tor skompilovať, otestovať a bezpečne aktivovať;
- Dependency Manager zobrazuje skutočný stav a schválené inštalácie fungujú s fail-closed politikou;
- self-contained balík vrátane aktuálneho peer kódu prejde kvalifikáciou na čistom Windows bez preinštalovaného SDK, .NET alebo MSYS2;
- bezpečnostný review, offline regresie, schválené verejné E2E a dlhodobé testy majú explicitne archivované dôkazy;
- žiadny blocker/HIGH zostávajúci na ceste používateľsky deklarovaných kľúčových funkcií nie je ticho ignorovaný.

**Embedded fáza sa znovu otvorí až po novom explicitnom rozhodnutí používateľa.**
