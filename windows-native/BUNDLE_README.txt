1. Nainštaluj MSYS2 do C:\msys64. V MSYS2 UCRT64 spusti pacman -Syu.
2. Dvojklik na COMPILE.cmd. Po dokončení vznikne priečinok dist.
3. Čistý dist skopíruj na oba PC pred prvým spustením.
4. Na oboch PC spusti iba TEST.cmd. Vyber PC A alebo PC B.
5. Počkaj na Tor 100 %. V okne sa zobrazí vlastná onion adresa.
6. Na každom PC vlož adresu druhého PC. Očakávaj PASS: 1 MiB tam aj späť, rovnaký SHA-256.
7. Enter zopakuje test. Q ukončí Tor aj server. Prvé spojenie môže chvíľu trvať.

Server používa 8081, SOCKS 9150, onion port 80. Oba PC potrebujú internet.
Adresy a kľúče vzniknú v onion-service. Medzi PC kopíruj iba adresu, nikdy kľúče.
