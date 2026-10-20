# Aurum

A five-reel console slot machine with 15 paylines, animated spins, and an integrated anti-cheat panel. Written in C# by Sathariel. All wagers use play credits.

## Gameplay

Each spin produces a 5 × 3 board. All 15 fixed paylines are included in the selected bet and evaluated from the leftmost reel. A line wins when at least three consecutive symbols match; a different symbol or scatter ends the sequence. Multiple winning lines pay independently.

Start with $1,000 and add $500 at any time. Paid spins deduct the bet before the reels roll, regardless of the outcome.

### Paytable

`A`, `K`, `3`, `4`, and `5` use the same base payout. `W` substitutes for any regular symbol; `S` is a scatter and does not substitute or pay on a line.

| Winning combination | 3 symbols | 4 symbols | 5 symbols |
| :--- | ---: | ---: | ---: |
| Matching regular symbols | 0.30× bet | 0.40× bet | 0.50× bet |
| Matching regular symbols with wilds | 0.60× bet | 0.80× bet | 1.00× bet |
| Wilds only | 0.30× bet | 0.40× bet | 0.50× bet |

Multipliers are per winning line and use the **total selected bet**, not an additional wager per line. The base calculation is:

```text
line win = matched symbols × 1.5 × (total bet / 15)
```

A combination containing both wilds and regular symbols receives a **2× multiplier**. An all-wild combination uses the base payout. Wilds can appear at the start of a line.

For a **$3 bet**, three matching regular symbols pay **$0.90** on one line. Substituting a wild raises that line's payout to **$1.80**. During a free spin, the same mixed-wild combination pays **$5.40**.

All line wins are added together, then rounded to cents using midpoint rounding away from zero. Payouts are credited after the spin; they do not include a separate refund of the wager.

### Scatters and free spins

Three or more `S` symbols anywhere on the board trigger a treasure choice. The selected treasure pays **5–14× the bet** and awards **15 free spins**.

Free spins do not deduct a wager. Line payouts receive a **3× multiplier**, which stacks with the mixed-wild multiplier for **6× the base line payout**. The treasure payout itself is not tripled. Additional scatter bonuses can award more free spins.

A bonus can also be purchased for **50× the current bet**, awarding 15 free spins without an immediate treasure payout.

## Controls

| Key | Action |
| :--- | :--- |
| **Space** | Spin |
| **↑ / ↓** | Adjust bet |
| **C** | Add $500 |
| **F** | Buy 15 free spins |
| **Tab** | Toggle the anti-cheat panel |
| **PgUp / PgDn** | Browse connected processes |
| **H** | Show game rules |
| **Q / Esc** | Return to the menu |

## Anti-cheat

The anti-cheat combines process-handle monitoring with balance integrity checks. Its status and process list are rendered inside the game UI.

### Handle monitoring and trust validation

On each scan, `NtQuerySystemInformation` retrieves an extended system-handle snapshot. The scanner identifies the game's process object and matches external handles against that object, using full-width process IDs and handle values.

Each matching handle is associated with its owning process. The scanner combines that process's access flags and counts its handles. Trust is evaluated once per owner within each scan:

- **Recognized system processes:** The executable name must be on the allowlist, and its directory must match the Windows system directory.
- **Visual Studio tools:** The supported debug console and data collectors must pass `WinVerifyTrust`, retain their expected original filename, and have a signing certificate identifying Microsoft Corporation.
- **Other applications:** Readable process identities that fail those checks are listed as other apps.
- **Unverified processes:** If the executable path cannot be queried, the owner is listed as not verified rather than assumed trusted.

Recognized processes remain visible in the process list. The game's own handles and reserved system PIDs are excluded. Administrator privileges improve visibility into other processes.

**An attachment or read/write permission alone never triggers punishment.** Handle flags describe granted access; they do not establish that an RPM or WPM call occurred.

### Balance storage and protection

The balance is stored in a dedicated **4 KB page** allocated with `VirtualAlloc`. The page normally has `PAGE_NOACCESS` protection. Legitimate balance operations briefly switch it to `PAGE_READWRITE`, access the stored values, and restore `PAGE_NOACCESS`.

Each legitimate write generates a new 64-bit key. The balance's IEEE 754 bit pattern and a consistency guard are stored as:

```text
encoded balance = balance bits XOR key
encoded guard   = (balance bits + key) XOR key
```

The key is stored in the same protected page. The guard calculation uses 64-bit wraparound arithmetic. Wallet operations and integrity checks share a lock so legitimate game updates do not race with the scanner.

### Tamper detection and punishment

Whenever the game reads the balance, it decodes both values and verifies:

```text
decoded balance bits + key == decoded guard
```

The background scanner also performs this check on each cycle, with a 250 ms pause between scans. A failed check indicates that the stored values no longer form a valid balance record.

The game then resets the balance to **$0**, writes a fresh key and guard, restores page protection, and records the event in the anti-cheat panel. This response is independent of the handle owner's trust classification: even a recognized process does not exempt a corrupted balance from validation.

The mechanism detects inconsistent edits; it cannot attribute the write to a particular process. XOR encoding is obfuscation, not cryptographic encryption. Page protection blocks ordinary access while active, but it is not a security boundary against a sufficiently privileged process. Coordinated changes that preserve the balance, key, and guard relationship can evade the current check.
