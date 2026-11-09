#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

missing=()
for item in python3 qemu-system-arm curl; do
    if ! command -v "$item" >/dev/null 2>&1; then missing+=("$item"); fi
done
if (( ${#missing[@]} )); then
    printf 'Chýbajú balíky: %s\n' "${missing[*]}"
    if ! command -v apt-get >/dev/null 2>&1; then
        printf 'Nainštaluj uvedené balíky a spusti balík znova.\n' >&2
        exit 1
    fi
    read -r -p 'Nainštalovať ich v Ubuntu vo WSL? [a/N] ' answer || answer=n
    case "$answer" in
        a|A) sudo apt-get update && sudo apt-get install "${missing[@]}" ;;
        *) exit 1 ;;
    esac
fi

compatible=true
if ! python3 -c 'import sys; sys.exit(sys.version_info < (3, 11))'; then
    printf 'Python je príliš starý: potrebný je Python 3.11 alebo novší.\n'
    compatible=false
fi
machines=$(qemu-system-arm -machine help 2>/dev/null) || machines=''
if ! grep -Eq '^[[:space:]]*virt-8\.2[[:space:]]' <<< "$machines"; then
    printf 'QEMU nepodporuje virt-8.2: potrebný je QEMU ARM 8.2 alebo novší.\n'
    compatible=false
fi

if [[ "$compatible" != true ]]; then
    printf '\nTest sa zatiaľ nespustí. Staršie Ubuntu sa rozpoznalo, ale jeho nástroje nestačia.\n'
    printf 'Ubuntu 24.04 alebo novšie poskytuje požadované balíky.\n'
    printf 'Aktualizácia distribúcie mení systém. Najprv si zálohuj súbory vo WSL.\n'
    printf 'Alternatíva: vo Windows nainštaluj samostatné Ubuntu: wsl --install -d Ubuntu-24.04\n'
    read -r -p 'Spustiť sprievodcu aktualizáciou tohto Ubuntu? [a/N] ' answer || answer=n
    case "$answer" in
        a|A)
            if ! command -v do-release-upgrade >/dev/null 2>&1; then
                sudo apt-get update
                sudo apt-get install update-manager-core
            fi
            sudo do-release-upgrade
            printf 'Po dokončení aktualizácie a prípadnom reštarte WSL spusti balík znova.\n'
            exit 2 ;;
        *) printf 'Bez zmien v systéme. Použi novšie Ubuntu alebo aktualizuj nástroje.\n'; exit 1 ;;
    esac
fi

printf 'Python a QEMU spĺňajú požiadavky testu.\n'
if [[ "${1:-}" == --check ]]; then exit 0; fi
exec python3 ./run.py "$@"
