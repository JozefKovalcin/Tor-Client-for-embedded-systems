TOR CAPACITY32 | TEST NA DVOCH PC

Oba počítače potrebujú prístup na internet. Vo Windows potrebuješ WSL2 s Ubuntu.
Spúšťač rozpozná všetky nainštalované Ubuntu podľa /etc/os-release vrátane
starších verzií a premenovaných distribúcií. Pri viacerých ponúkne výber.
Potrebný je QEMU ARM 8.2 alebo novší s virt-8.2 a Python 3.11 alebo novší.

1. Na oba počítače skopíruj tento ZIP a celý ho rozbaľ.
2. Na PC B otvor Spustit-Tor-Test.cmd, zvoľ B a počkaj na adresu .onion.
3. Na PC A otvor ten istý súbor, zvoľ A a vlož adresu z PC B.
4. Výsledok PASS na PC A potvrdzuje, že sa všetky bajty vrátili bez zmeny.
   Po teste ukonči spúšťač na PC B klávesmi Ctrl+C.

Pri prvom spustení ponúkne doinštalovanie chýbajúcich balíkov
QEMU, Python alebo curl vo WSL. Inštalácia môže vyžadovať heslo do Ubuntu.
Ak WSL2 chýba, pozri https://learn.microsoft.com/windows/wsl/install.
Na Linuxe alebo priamo vo WSL otvor priečinok balíka a spusti bash run-wsl.sh.

Pred spustením testu sa overí verzia Pythonu a podpora virt-8.2 v QEMU.
Ak sú príliš staré, spúšťač ponúkne sprievodcu aktualizáciou Ubuntu.
Bez potvrdenia nemení systém. Pred aktualizáciou si zálohuj súbory vo WSL.
Alternatívou je samostatná novšia inštalácia: wsl --install -d Ubuntu-24.04.
Po aktualizácii a prípadnom reštarte WSL spusti balík znova.
Samotné predpoklady overíš príkazom bash run-wsl.sh --check.

Balík obsahuje zImage a disk.img z projektového buildu ARMv7 capacity32
20261105T190223Z-7002ec13. Spúšťač overí SHA-256 oboch súborov aj binárky
/usr/bin/tor v QEMU. Tor na oboch PC beží z tohto obrazu. Dočasná vrstva
QEMU zachová pôvodné obrazy a pri ukončení zahodí testovaciu onion identitu.
Test overuje TCP prenos cez verejný Tor medzi dvoma QEMU inštanciami.
