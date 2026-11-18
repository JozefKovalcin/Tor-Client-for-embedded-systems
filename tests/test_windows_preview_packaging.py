import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / 'desktop/packaging/pack_gui_preview.py'
SPEC = importlib.util.spec_from_file_location('pack_gui_preview', SCRIPT)
preview = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(preview)


class WindowsPreviewPackagingTests(unittest.TestCase):
    MANAGED = 'lib/net10.0/Renderer.dll'
    NATIVE = 'runtimes/win-x64/native/renderer.dll'

    def setUp(self):
        self.deps = {'libraries': {'Renderer/1': {'type': 'package'}}}
        self.entry = {
            'runtime': {self.MANAGED: {}},
            'runtimeTargets': {
                self.NATIVE: {'rid': 'win-x64', 'assetType': 'native'},
                'runtimes/win-arm64/native/renderer.dll': {'rid': 'win-arm64', 'assetType': 'native'},
                'runtimes/linux-x64/native/renderer.so': {'rid': 'linux-x64', 'assetType': 'native'},
            },
        }
        self.expected = {'Renderer/1': [self.MANAGED, self.NATIVE]}

    def validate(self):
        preview.check_package_assets(self.deps, {'Renderer/1': preview.preview_assets(self.entry)}, self.expected)

    def test_matches_reviewed_windows_assets(self):
        self.validate()

    def test_omitted_native_asset_is_rejected(self):
        del self.entry['runtimeTargets'][self.NATIVE]
        with self.assertRaisesRegex(ValueError, 'differs: Renderer/1'):
            self.validate()

    def test_omitted_package_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'differs: Renderer/1'):
            preview.check_package_assets(self.deps, {}, self.expected)

    def test_unexpected_managed_asset_is_rejected(self):
        self.entry['runtime']['lib/net10.0/Other.dll'] = {}
        with self.assertRaisesRegex(ValueError, 'differs: Renderer/1'):
            self.validate()
