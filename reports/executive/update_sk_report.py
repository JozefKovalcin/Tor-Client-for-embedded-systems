"""Clarify the performance page and launcher note in the supplied Slovak report.

Authoring dependency: PyMuPDF. The measured pages are preserved.
"""
import argparse
from pathlib import Path
import pymupdf

NOTE = (
    'Spúšťač nájde aj staršie Ubuntu. Ak Python alebo QEMU nestačí,\n'
    'ponúkne aktualizáciu. Inštalácia aj aktualizácia vyžadujú potvrdenie.'
)

TITLE = 'Porovnanie výkonu pri 128 a 256 MiB RAM'


def performance_page(document, font):
    page = document[3]
    if len(page.search_for('Obe skupiny so 128 MiB nesplnili výkonové kritériá')) != 1:
        raise ValueError('expected the original performance-page layout')
    page.add_redact_annot(pymupdf.Rect(44, 60, 916, 94), fill=(1, 1, 1))
    page.add_redact_annot(pymupdf.Rect(44, 397, 916, 500), fill=(1, 1, 1))
    page.apply_redactions(images=0, graphics=0)
    page.insert_font(fontname='SegoeBody', fontfile=str(font))
    page.insert_font(fontname='SegoeBold', fontfile=str(font.with_name('segoeuib.ttf')))
    navy = (0.071, 0.176, 0.271)
    teal = (0.0, 0.49, 0.45)

    def text(rect, content, size, bold=False, color=navy):
        remaining = page.insert_textbox(pymupdf.Rect(rect), content, fontsize=size,
                                        fontname='SegoeBold' if bold else 'SegoeBody',
                                        color=color, lineheight=1.16)
        if remaining < 0:
            raise ValueError('performance-page text does not fit: ' + content)

    text((45, 57, 915, 96), TITLE, 24.75, bold=True)
    cards = [
        ((45, 398, 474, 450), 'ARMv7 / 128 MiB',
         'Pripojenie k službe .onion: 4,32 s, povolené najviac 3,78 s.\n'
         'Prenos cez Tor: 143,80 KiB/s, požadované minimum 153,33 KiB/s.'),
        ((486, 398, 915, 450), 'x86-64 / 128 MiB',
         'Lokálne TCP nedosiahlo požadovanú rýchlosť.\n'
         'Pripojenie k sieti Tor trvalo dlhšie, než povoľoval limit.'),
    ]
    for bounds, heading, body in cards:
        x0, y0, x1, y1 = bounds
        page.draw_rect(pymupdf.Rect(bounds), color=None, fill=(1, 0.969, 0.91))
        page.draw_rect(pymupdf.Rect(x0, y0, x0 + 3, y1), color=None, fill=(0.898, 0.682, 0.259))
        text((x0 + 12, y0 + 3, x1 - 10, y0 + 18), heading, 10.5, bold=True)
        text((x0 + 12, y0 + 18, x1 - 10, y1), body, 10.5)

    page.draw_rect(pymupdf.Rect(45, 455, 915, 500), color=None, fill=(0.878, 0.949, 0.937))
    page.draw_rect(pymupdf.Rect(45, 455, 49, 500), color=None, fill=teal)
    text((57, 457, 903, 474), 'Pravidlo 20 % a kolísanie meraní', 11, bold=True, color=teal)
    for baseline, content, bold in [
        (481.5, 'Porovnávame 128 MiB s 256 MiB na rovnakej architektúre. '
         'Čas smie byť najviac o 20 % dlhší, rýchlosť o 20 % nižšia.', False),
        (495.5, 'Hodnoty sú mediány z troch opakovaní. '
         'Prenos cez Tor na ARMv7 / 128 MiB kolísal od 90,80 do 299,40 KiB/s.', True),
    ]:
        face = font.with_name('segoeuib.ttf') if bold else font
        if pymupdf.Font(fontfile=str(face)).text_length(content, fontsize=10) > 846:
            raise ValueError('methodology line exceeds panel width')
        page.insert_text((57, baseline), content, fontsize=10,
                         fontname='SegoeBold' if bold else 'SegoeBody', color=navy)


def update(source, output, font):
    if output.exists():
        raise ValueError('output exists; preserve it and choose a fresh path')
    document = pymupdf.open(source)
    if len(document) != 6:
        raise ValueError('expected the supplied six-page Slovak report')
    performance_page(document, font)
    page = document[5]
    matches = page.search_for('Ak chýba')
    if len(matches) != 1:
        raise ValueError('launcher note is ambiguous or already updated')
    # This rectangle contains only the old two-line prerequisites note.
    note_rect = pymupdf.Rect(60, 307, 459, 337)
    old_note = page.get_textbox(note_rect)
    if 'Ak chýba' not in old_note or 'sudo' not in old_note:
        raise ValueError('the supplied layout changed')
    background = tuple(value / 255 for value in page.get_pixmap().pixel(61, 307)[:3])
    page.add_redact_annot(note_rect, fill=background)
    page.apply_redactions(images=0, graphics=0)
    page.insert_font(fontname='SegoeNote', fontfile=str(font))
    remaining = page.insert_textbox(pymupdf.Rect(60.75, 307, 459, 345), NOTE,
                                    fontname='SegoeNote', fontsize=10.5,
                                    color=(0.267, 0.384, 0.482), lineheight=1.25)
    if remaining < 0:
        raise ValueError('updated note does not fit')
    output.parent.mkdir(parents=True, exist_ok=True)
    document.subset_fonts()
    document.save(output, garbage=4, deflate=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--font', type=Path, required=True)
    args = parser.parse_args()
    update(args.input, args.output, args.font)
