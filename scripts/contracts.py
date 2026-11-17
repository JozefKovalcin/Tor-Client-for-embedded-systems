#!/usr/bin/env python3
"""Offline v1 contract validation using an explicit JSON Schema subset.

This is not a general JSON Schema implementation. Unsupported keywords/formats,
external references and unknown versions fail closed. No network, subprocess,
installation, legacy report migration or file writes occur in this module.
"""
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
SCHEMA_DIR = ROOT / "config/contracts/v1"
CONTRACT_NAMES = (
    "TargetDescriptor", "DependencyCheck", "BuildRequest", "TaskProgress",
    "BuildArtifact", "DeviceInfo", "TestResult", "EvidenceManifest",
)
KEYWORDS = frozenset({
    "$schema", "$id", "$defs", "$ref", "title", "description", "type",
    "properties", "required", "additionalProperties", "items", "enum", "const",
    "minimum", "maximum", "exclusiveMinimum", "minLength", "maxLength",
    "minItems", "uniqueItems", "pattern", "format", "anyOf", "allOf", "not",
})
MAX_DEPTH = 64
MAX_JSON_BYTES = 2 * 1024 * 1024


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON member")
        result[key] = value
    return result


def _invalid_constant(_value):
    raise ValueError("nonfinite JSON number")


def read_json_file(path):
    """Read a bounded JSON document, rejecting duplicate keys and NaN/Infinity."""
    with Path(path).open("rb") as stream:
        data = stream.read(MAX_JSON_BYTES + 1)
    if len(data) > MAX_JSON_BYTES:
        raise ValueError("JSON document exceeds 2 MiB limit")
    return json.loads(data.decode("utf-8"), object_pairs_hook=_unique_object,
                      parse_constant=_invalid_constant)


def _resolve_ref(reference, root):
    if not isinstance(reference, str) or not re.fullmatch(r"#/\$defs/[A-Za-z][A-Za-z0-9_]*", reference):
        raise ValueError("only in-document #/$defs/Name schema references are supported")
    name = reference.rsplit("/", 1)[1]
    try:
        return root["$defs"][name]
    except KeyError as exc:
        raise ValueError("unresolved local schema reference") from exc


def _audit_schema(schema, root, depth=0):
    if depth > MAX_DEPTH or not isinstance(schema, (dict, bool)):
        raise ValueError("invalid or excessively nested schema")
    if isinstance(schema, bool):
        return
    unsupported = set(schema) - KEYWORDS
    if unsupported:
        raise ValueError("unsupported schema keywords: " + ", ".join(sorted(unsupported)))
    if "$ref" in schema:
        _resolve_ref(schema["$ref"], root)
    if "format" in schema and schema["format"] != "date-time":
        raise ValueError("unsupported schema format")
    if "pattern" in schema:
        re.compile(schema["pattern"])
    for key in ("properties", "$defs"):
        for child in schema.get(key, {}).values():
            _audit_schema(child, root, depth + 1)
    for key in ("items", "additionalProperties", "not"):
        if key in schema:
            _audit_schema(schema[key], root, depth + 1)
    for key in ("anyOf", "allOf"):
        for child in schema.get(key, []):
            _audit_schema(child, root, depth + 1)


def load_schema(name):
    """Load one bundled schema by allowlisted name; never resolve remote URLs."""
    if name not in CONTRACT_NAMES:
        raise ValueError("unknown contract name")
    schema = read_json_file(SCHEMA_DIR / (name + ".schema.json"))
    if schema.get("$schema") != "https://json-schema.org/draft/2020-12/schema":
        raise ValueError("unsupported schema dialect")
    version = schema.get("properties", {}).get("schemaVersion", {}).get("const")
    if type(version) is not int or version != 1:
        raise ValueError("unsupported bundled schema version")
    _audit_schema(schema, schema)
    return schema


def _same(left, right):
    # JSON booleans are not numbers, despite Python's True == 1 behavior.
    if isinstance(left, bool) or isinstance(right, bool):
        return type(left) is type(right) and left == right
    return left == right


def _is_type(value, kind):
    return {
        "object": lambda: isinstance(value, dict),
        "array": lambda: isinstance(value, list),
        "string": lambda: isinstance(value, str),
        "boolean": lambda: isinstance(value, bool),
        "null": lambda: value is None,
        # JSON Schema defines integers by numeric value, not lexical spelling
        # or Python's int/float storage type (1, 1.0 and 1e0 are equivalent).
        "integer": lambda: not isinstance(value, bool) and (
            isinstance(value, int) or
            isinstance(value, float) and math.isfinite(value) and value.is_integer()),
        "number": lambda: isinstance(value, (int, float)) and not isinstance(value, bool),
    }[kind]()


def _json_errors(value, path="$", ancestors=None, depth=0):
    if depth > MAX_DEPTH:
        return [path + ": document nesting exceeds limit"]
    if isinstance(value, float) and not math.isfinite(value):
        return [path + ": nonfinite numbers are forbidden"]
    if value is None or isinstance(value, (str, int, float, bool)):
        return []
    if not isinstance(value, (dict, list)):
        return [path + ": not a JSON value"]
    ancestors = set() if ancestors is None else ancestors
    if id(value) in ancestors:
        return [path + ": cyclic document"]
    ancestors = ancestors | {id(value)}
    errors = []
    if isinstance(value, dict):
        if any(not isinstance(key, str) for key in value):
            return [path + ": JSON object keys must be strings"]
        items = value.items()
    else:
        items = enumerate(value)
    for key, child in items:
        errors.extend(_json_errors(child, path + "/" + str(key), ancestors, depth + 1))
    return errors


def _matches(schema, value, root, path, depth):
    return not _validate(schema, value, root, path, depth + 1)


def _validate(schema, value, root, path="$", depth=0):
    if depth > MAX_DEPTH:
        return [path + ": schema nesting exceeds limit"]
    if isinstance(schema, bool):
        return [] if schema else [path + ": value is forbidden"]
    errors = []
    if "$ref" in schema:
        errors.extend(_validate(_resolve_ref(schema["$ref"], root), value, root, path, depth + 1))
    if "anyOf" in schema and not any(_matches(s, value, root, path, depth) for s in schema["anyOf"]):
        errors.append(path + ": does not match any allowed shape")
    for child in schema.get("allOf", []):
        errors.extend(_validate(child, value, root, path, depth + 1))
    if "not" in schema and _matches(schema["not"], value, root, path, depth):
        errors.append(path + ": forbidden value or reference")
    types = schema.get("type")
    if types is not None:
        types = [types] if isinstance(types, str) else types
        if not any(_is_type(value, kind) for kind in types):
            return errors + [path + ": wrong JSON type"]
    if "enum" in schema and not any(_same(value, option) for option in schema["enum"]):
        errors.append(path + ": value outside allowed enumeration")
    if "const" in schema and not _same(value, schema["const"]):
        errors.append(path + ": incorrect constant or version")
    if isinstance(value, dict):
        for key in schema.get("required", []):
            if key not in value:
                errors.append(path + "/" + key + ": required member missing")
        properties = schema.get("properties", {})
        for key, child in value.items():
            child_path = path + "/" + key
            if key in properties:
                errors.extend(_validate(properties[key], child, root, child_path, depth + 1))
            elif "additionalProperties" in schema:
                errors.extend(_validate(schema["additionalProperties"], child, root, child_path, depth + 1))
    if isinstance(value, list):
        if len(value) < schema.get("minItems", 0):
            errors.append(path + ": too few items")
        if schema.get("uniqueItems"):
            canonical = [json.dumps(v, sort_keys=True, allow_nan=False) for v in value]
            if len(set(canonical)) != len(canonical):
                errors.append(path + ": duplicate items")
        if "items" in schema:
            for index, child in enumerate(value):
                errors.extend(_validate(schema["items"], child, root, path + "/" + str(index), depth + 1))
    if isinstance(value, str):
        if len(value) < schema.get("minLength", 0) or len(value) > schema.get("maxLength", math.inf):
            errors.append(path + ": invalid string length")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.append(path + ": invalid string pattern")
        if schema.get("format") == "date-time":
            try:
                if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|\+00:00)", value):
                    raise ValueError("UTC timestamp required")
                parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
                if parsed.utcoffset() != timezone.utc.utcoffset(parsed):
                    raise ValueError("UTC timestamp required")
            except ValueError:
                errors.append(path + ": invalid UTC date-time")
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if "minimum" in schema and value < schema["minimum"]:
            errors.append(path + ": below minimum")
        if "maximum" in schema and value > schema["maximum"]:
            errors.append(path + ": above maximum")
        if "exclusiveMinimum" in schema and value <= schema["exclusiveMinimum"]:
            errors.append(path + ": must exceed exclusive minimum")
    return errors


def _metric_errors(metrics, path):
    errors = []
    for name, metric in metrics.items():
        here = path + "/" + name
        if metric["value"] is None:
            if not metric["unavailableReason"]:
                errors.append(here + ": null measurement needs unavailableReason")
        elif not metric["method"] or metric["unavailableReason"] is not None:
            errors.append(here + ": measured value needs method and null unavailableReason")
        expected_unit = None
        if name.endswith("_bytes_per_second"):
            expected_unit = "B/s"
        elif name.endswith("_bytes"):
            expected_unit = "B"
        elif name.endswith("_seconds"):
            expected_unit = "s"
        elif name.endswith("_percent"):
            expected_unit = "%"
        if expected_unit is not None and metric["unit"] != expected_unit:
            errors.append(here + ": unit contradicts metric name")
    return errors


def _semantic_errors(name, value):
    errors = []
    if name == "TargetDescriptor":
        kind = value["kind"]
        if kind == "windows-native" and (value["os"], value["architecture"], value["abi"]) != ("windows", "x86_64", "ucrt64"):
            errors.append("$: Windows target requires Windows x86_64/UCRT64")
        if kind == "linux-native" and ((value["os"], value["architecture"]) != ("linux", "x86_64") or
                                       value["abi"] not in ("glibc", "musl") or value["abiVersion"] is None):
            errors.append("$: native Linux descriptor requires explicit Linux x86_64 ABI/version")
        if value["id"] == "linux-x86_64-glibc239-v1" and (kind, value["os"], value["architecture"], value["abi"], value["abiVersion"]) != ("linux-native", "linux", "x86_64", "glibc", "2.39"):
            errors.append("$: selected native Linux v1 policy requires Linux x86_64/glibc 2.39")
        if kind in ("buildroot-qemu", "pico-yocto") and value["os"] != "linux":
            errors.append("$: embedded and QEMU targets require Linux")
        if kind == "buildroot-qemu" and value["executionScope"] != "simulation":
            errors.append("$: QEMU target must be labelled simulation")
        if kind == "pico-yocto" and value["architecture"] != "armv7":
            errors.append("$: PICO v1 target requires ARMv7")
        if value["certainty"] == "verified" and value["executionScope"] == "physical":
            if not all(value[key] for key in ("boardPart", "carrierRevision", "dtbSha256", "evidenceRefs")):
                errors.append("$: verified physical target needs board/carrier/DTB evidence")
    elif name == "DependencyCheck":
        status = value["status"]
        if status == "AVAILABLE":
            if value["certainty"] != "verified":
                errors.append("$: AVAILABLE requires verified compatible probe evidence")
            if value["expectedSha256"] is not None and value["expectedSha256"] != value["observedSha256"]:
                errors.append("$: AVAILABLE cannot omit or mismatch an expected digest")
        if status == "BLOCKED" and value["certainty"] != "BLOCKED":
            errors.append("$: blocked probe requires BLOCKED certainty")
        if status == "UNKNOWN" and value["certainty"] == "verified":
            errors.append("$: unknown compatibility cannot be verified")
    elif name == "BuildRequest":
        if value["imageProfile"] is not None and value["existingProfileId"] is not None:
            errors.append("$: Yocto and existing Buildroot profiles are mutually exclusive")
        if value["adapterId"].startswith("native-linux") and (value["imageProfile"] is not None or value["existingProfileId"] is not None):
            errors.append("$: native Linux adapter has no image/Buildroot profile")
    elif name == "TaskProgress":
        completed, total, unit = (value[key] for key in ("completed", "total", "unit"))
        if (completed is None, total is None, unit is None) not in ((True, True, True), (False, False, False)):
            errors.append("$: progress must be fully indeterminate or have completed/total/unit")
        if completed is not None and total is not None:
            if completed > total:
                errors.append("$: completed exceeds total")
            if unit == "%" and total != 100:
                errors.append("$: percentage total must be 100")
        if value["retryOfTaskId"] == value["taskId"] or value["parentTaskId"] == value["taskId"]:
            errors.append("$: retry/parent cannot refer to same task")
        if value["rawExitCode"] is not None and value["terminationSignal"] is not None:
            errors.append("$: report observed exit or signal, not both")
        if value["state"] == "interrupted" and value["reasonCode"] not in ("app-crash", "runner-crash", "runner-lost"):
            errors.append("$: interrupted task requires explicit crash/lost-runner reason")
        if value["state"] == "succeeded" and (value["rawExitCode"] not in (None, 0) or value["terminationSignal"] is not None):
            errors.append("$: succeeded task cannot have failed process exit")
    elif name == "BuildArtifact":
        if value["signature"]["status"] == "verified" and not all(value["signature"][key] for key in ("verifierKeyId", "evidenceRef")):
            errors.append("$: verified signature requires key and verification evidence")
        if value["targetId"] not in value["compatibleTargetIds"]:
            errors.append("$: artifact target absent from compatibility list")
    elif name == "DeviceInfo":
        if value["pairing"] == "paired" and value["peerFingerprint"] is None:
            errors.append("$: paired device requires peer fingerprint")
        if value["certainty"] == "verified" and not value["evidenceRefs"]:
            errors.append("$: verified device requires evidence")
        if value["storageBytes"] is not None and value["stateFreeBytes"] is not None and value["stateFreeBytes"] > value["storageBytes"]:
            errors.append("$: free state exceeds total storage")
    elif name == "TestResult":
        errors.extend(_metric_errors(value["metrics"], "$/metrics"))
        if value["status"] == "NOT RUN" and any(value[key] is not None for key in ("startedAtUtc", "durationSeconds", "rawExitCode")):
            errors.append("$: NOT RUN cannot claim observed execution metadata")
        if value["status"] == "PASS" and (value["startedAtUtc"] is None or value["durationSeconds"] is None):
            errors.append("$: PASS requires execution timestamp and duration")
        if value["status"] == "PASS" and value["rawExitCode"] not in (None, 0):
            errors.append("$: PASS cannot have observed failed process exit")
        if value["status"] == "NOT RUN" and any(metric["value"] is not None for metric in value["metrics"].values()):
            errors.append("$: NOT RUN cannot claim observed measurements")
    elif name == "EvidenceManifest":
        errors.extend(_semantic_errors("TargetDescriptor", value["target"]))
        errors.extend(_metric_errors(value["metrics"], "$/metrics"))
        for kind, items in (("BuildArtifact", value["artifacts"]), ("TestResult", value["tests"])):
            for index, item in enumerate(items):
                errors.extend(f"$/{kind}/{index}: {error}" for error in _semantic_errors(kind, item))
                if item["targetId"] != value["target"]["id"]:
                    errors.append("$: nested evidence target does not match manifest target")
                if kind == "TestResult":
                    for key in ("policySha256", "protocolId"):
                        if item[key] != value[key]:
                            errors.append("$: nested test " + key + " contradicts manifest")
                    if item["scope"] != value["target"]["executionScope"]:
                        errors.append("$: nested test scope contradicts target execution scope")
        test_ids = [item["testId"] for item in value["tests"]]
        if len(set(test_ids)) != len(test_ids):
            errors.append("$: duplicate test IDs in one run")
        if value["comparison"]["status"] == "ACCEPTED":
            policy = read_json_file(ROOT / "config/policy.json")
            tests = {item["testId"]: item for item in value["tests"]}
            if not value["comparison"]["referenceId"] or not value["toolchainManifestRef"]:
                errors.append("$: ACCEPTED requires reference and toolchain evidence")
            if not value["artifacts"]:
                errors.append("$: ACCEPTED requires produced artifact evidence")
            if any(tests.get(test, {}).get("status") != "PASS" for test in policy["required_tests"]):
                errors.append("$: ACCEPTED requires all nine mandatory tests PASS")
            if any(value["metrics"].get(metric, {}).get("value") is None for metric in policy["required_metrics"]):
                errors.append("$: ACCEPTED cannot omit required measurements")
            if any(item["status"] != "PASS" for item in value["tests"]):
                errors.append("$: ACCEPTED cannot contain incomplete or failed tests")
            online = {"socks5", "public_bootstrap", "onion_v3", "two_node",
                      "bidirectional_integrity", "reconnection"}
            if any(tests.get(test, {}).get("mode") != "online" for test in online):
                errors.append("$: ACCEPTED public assertions require online execution mode")
    return errors


def validate_contract(name, instance):
    """Return diagnostics; an empty list means structurally/semantically valid.

    Contract validity is not an executed test PASS, source signature check,
    measurement comparison, filesystem containment or hardware qualification.
    """
    try:
        schema = load_schema(name)
        errors = _json_errors(instance)
        if errors:
            return errors
        errors = _validate(schema, instance, schema)
        return errors if errors else _semantic_errors(name, instance)
    except (OSError, ValueError, TypeError, KeyError, RecursionError) as exc:
        return ["contract validation blocked: " + str(exc)]


def require_valid(name, instance):
    """Raise ValueError on invalid or unsupported input; do not mutate it."""
    errors = validate_contract(name, instance)
    if errors:
        raise ValueError("; ".join(errors))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("contract", choices=CONTRACT_NAMES)
    parser.add_argument("document", type=Path)
    args = parser.parse_args(argv)
    try:
        errors = validate_contract(args.contract, read_json_file(args.document))
    except (OSError, ValueError, UnicodeError, RecursionError) as exc:
        errors = ["unable to read JSON document: " + str(exc)]
    print(json.dumps({"schemaVersion": 1, "contract": args.contract,
                      "valid": not errors, "errors": errors}, allow_nan=False))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
