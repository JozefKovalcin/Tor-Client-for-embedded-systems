# Tor pre Windows

1. Nainštaluj MSYS2 do `C:\msys64`. V UCRT64 spusti `pacman -Syu` a prípadne zopakuj po reštarte terminálu.
2. Spusti `COMPILE.cmd`. Výsledok je v `dist`.
3. Čistý `dist` skopíruj na oba PC pred prvým spustením.
4. Na oboch spusti iba `TEST.cmd`, vyber PC A alebo B a počkaj na Tor 100 %.
5. Vlož onion adresu druhého PC. Test overí 1 MiB tam aj späť a SHA-256. Enter opakuje, Q ukončí Tor aj server.

Server: **8081**, SOCKS: **9150**, onion port: **80**. Prenášaj iba onion adresu, nie kľúče.

Zdroje sa stiahnu do `%LOCALAPPDATA%\TorNativeBuild\downloads` a rozbalia do `<hash>/<run>/src`.
ZIP vytvoríš z koreňa repozitára: `python windows-native/scripts/create-clean-zip.py`.

[Technická kompilácia](BUILD.md) · [Pôvod zdrojov a oprava](PORTING.md) · [Vykonané testy](evidence/README.md)

Natívne výsledky sú z 12. novembra 2026 na Windows build 26100. Reálne dva PC a Windows 10 neboli osobitne overené. Linux/ARM a jeho historické výsledky zostávajú nezmenené.
