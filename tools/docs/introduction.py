"""Generate the book introduction from README, adapting its links for mdBook."""

from tools import ROOT

OUTPUT = ROOT / 'docs/src/introduction.md'
REPOSITORY = 'https://github.com/droserasprout/slopworld/blob/main/'


def main() -> None:
    text = (ROOT / 'README.md').read_text(encoding='utf-8')
    # Book links and HTML images are relative to docs/src rather than the repo root.
    text = text.replace('](docs/src/', '](').replace('src="docs/src/', 'src="')
    # Repository license files are not published as book pages.
    for path in ('LICENSE', 'licenses/README.md'):
        text = text.replace(f']({path})', f']({REPOSITORY}{path})')
    text = '<!-- Generated from README.md by just refresh-introduction. Do not edit. -->\n\n' + text
    # Avoid triggering a docs watcher when the generated content has not changed.
    if not OUTPUT.exists() or OUTPUT.read_text(encoding='utf-8') != text:
        OUTPUT.write_text(text, encoding='utf-8')


if __name__ == '__main__':
    main()
