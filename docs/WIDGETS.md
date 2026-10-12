# Writing and sharing Bunker widgets

A **community widget** adds buttons and fields to Bunker. It is a single `.json` file. It is **never code**: it can only
send the console commands written on its buttons, and Bunker shows you every one of them before you install it.

## Install a widget

In Bunker open **Tools → Widgets**, then:

| Button | What it does |
| --- | --- |
| **Browse library** | Lists the widgets published in this repository's [`widgets/`](../widgets) folder. |
| **Add from file…** | Installs a `.json` file from your PC. |
| **Add from link…** | Installs from an `https://` link, for example a raw GitHub file. |

Every install shows a review window with the author, the description and **every command the widget can send**.
Commands that can affect players or the server itself (kick, ban, restart, give-to-everyone…) are marked with `!`.

Installed widgets get their own tab on the Tools page and appear in the widget picker on the **Layout** page.

## The file format

```json
{
  "id": "resource-drop",
  "name": "Resource Drop",
  "author": "Your name",
  "version": "1.0.0",
  "description": "Reward everyone online with a resource.",
  "controls": [
    { "type": "choice", "id": "item", "label": "Resource", "options": ["wood", "stones", "scrap"] },
    { "type": "number", "id": "amount", "label": "Amount each", "default": 500, "min": 1, "max": 5000 },
    {
      "type": "button",
      "text": "Give to everyone",
      "confirm": "Give {amount} x {item} to EVERY player?",
      "commands": [
        "inventory.giveall {item} {amount}",
        "say \"Everyone just received {amount} {item}!\""
      ]
    }
  ]
}
```

### Top level

| Field | Required | Notes |
| --- | --- | --- |
| `id` | yes | 2–41 characters: lower-case letters, numbers, `-` and `_`. Must be unique. |
| `name` | yes | Up to 40 characters. Shown on the tab and in the Layout picker. |
| `author`, `version`, `description` | no | Shown in the review window and the library. |
| `controls` | yes | 1–30 controls, shown top to bottom. |

### Controls

| `type` | Fields | What the user sees |
| --- | --- | --- |
| `label` | `text` | A line of explanatory text. |
| `separator` | – | A thin divider line. |
| `text` | `id`, `label`, `placeholder` | A single-line text box. |
| `number` | `id`, `label`, `default`, `min`, `max` | A number box. The value is checked against `min` and `max`. |
| `choice` | `id`, `label`, `options` | A drop-down list (1–50 options). |
| `player` | `id`, `label` | A drop-down of the players online right now. |
| `button` | `text`, `commands`, `style`, `confirm` | Runs 1–10 commands. `style` is `primary` (default) or `ghost`. `confirm` asks first. |

Consecutive buttons are placed side by side.

### Placeholders

Inside a button's `commands` (and `confirm`) text, `{id}` is replaced by what the user entered in the control with that `id`.

* `{target}` for a `player` control is the player's **Steam ID**; `{target.name}` is their **name**.
* Text is cleaned first: quotes, semicolons, backslashes and control characters are removed, and it is cut to 200 characters.
* Numbers are checked against `min`/`max`; a `choice` must be one of its `options`.
* If a field is empty, the button refuses to run and tells the user which field to fill in.

## What widgets can not do

* They are data, not programs: no scripts, no network access, no files.
* They can not use commands that change RCON itself or who is an admin: anything starting `rcon.`, and
  `ownerid`, `moderatorid`, `removeowner`, `removemoderator`. Bunker refuses to install a widget that contains one.
* Every command is visible in the review window before installing, and marked `!` if it is a risky kind.

## Share your widget

1. Make sure the file passes in Bunker (**Tools → Widgets → Add from file…**).
2. Fork this repository and add your file to [`widgets/`](../widgets) (keep it under 64 KB).
3. Open a pull request. Describe what the widget does and which Rust commands it uses, and say where you checked that they work.

Widgets are reviewed by hand before they are merged, and anyone can still install widgets from other places using a link.
Always read the command list before you install a widget you did not write.
