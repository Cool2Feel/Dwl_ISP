# Templates Directory

This directory contains JSON template configuration files for the ResBinManager.

## Purpose

Template files define project-specific configuration values, feature rules, and override settings for different firmware projects.

## Supported Projects

- JT529X
- DC508J
- GX-T317BV200
- HM020F
- MKL_CM5
- MKL_DM15
- JRX_JT529X
- JRX_AX329X

## Template Format

```json
{
  "info": {
    "id": "TemplateId",
    "name": "Template Name",
    "description": "Template description",
    "version": "1.0"
  },
  "baseValues": {
    "CONFIG_ID_YEAR": 2026,
    "CONFIG_ID_LANGUAGE": 257
  },
  "projectOverrides": {
    "JT529X": {
      "CONFIG_ID_RESOLUTION": 396
    }
  },
  "featureRules": {}
}
```

## Usage

Templates are automatically loaded from:
1. `Templates/` directory (next to executable)
2. `Config/Templates/` directory
3. `%AppData%/ResBinManager/Templates/`
