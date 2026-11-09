"""Update only the launcher note on page six of the supplied Slovak report.

Authoring dependency: PyMuPDF. The measured pages are preserved.
"""
import argparse
from pathlib import Path
import pymupdf

NOTE = (
    'Spúšťač nájde aj staršie Ubuntu. Ak Python alebo QEMU nestačí,\n'
    'ponúkne aktualizáciu. Inštalácia aj aktualizácia vyžadujú potvrdenie.'
)


def update(source, output, font):
    if output.exists():
        raise ValueError('output exists; preserve it and choose a fresh path')
    document = pymupdf.open(source)
    if len(document) != 6:
        raise ValueError('expected the supplied six-page Slovak report')
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
