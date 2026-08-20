# Tutorial prompt copy

Every player-facing tutorial string, in the order the walkthrough presents them.
Source of truth: `TextFor` in `src/Onboarding/FirstSessionGuidanceController.cs`.

Two steps have a conditional variant, marked *(like this)* in the cell.

To rewrite: edit the **Text** column here and hand the file back; the copy is
reapplied to `TextFor` verbatim. Step ids and order are not copy — leave them alone.

| # | Step id | Text |
| --- | --- | --- |
| 1 | `grab_buddy` | Say hello. Press and hold the left mouse button on Buddy to pick him up, then fling him around the room. Let go when you have had your fun. |
| 2 | `open_inventory` | Time to go shopping. Open Inventory, up in the top-left corner — that is where every tool you own lives. |
| 3 | `purchase_baseball_bat` | This is the Inventory: everything you can buy and equip, all in one list. Your credits are counted up in the top-right corner, and playing with Buddy is what earns them — the rougher the play, the bigger the payout. Grab the Baseball Bat: buy it for 1 credit, or equip it if you already own it. Anything you buy is equipped straight away. |
| 4 | `charged_bat_hit` | Batter up. The bat is already in your hands. Hold right mouse to wind up, then let go and swing. Any amount of charge is enough for this lesson; a full charge hurts a lot more — and pays a lot more. |
| 5 | `unequip_tool` | Done with a tool? Press D to drop it. You are back to bare hands — and to pick it up again, just double-click the dropped tool. |
| 6 | `open_paint_buddy` | Buddy is looking a little plain. Open Paint ▸ Buddy and let us fix that. |
| 7 | `create_buddy` | *(slots free)* First, a buddy of your own to work on. Press + New Character, give him a name, and he is the one you paint from here on. <br>*(no slots left)* No free character slots left — so pick the buddy you want to work on from the Characters list instead. Everything after this works exactly the same. |
| 8 | `select_paint_brush` | Pick up the Brush — it is the one you will use most. |
| 9 | `select_paint_color` | Now pick any colour that takes your fancy. |
| 10 | `paint_buddy` | Go on, paint something across Buddy's torso. |
| 11 | `save_paint_buddy` | Happy with it? Save the character to keep your work. |
| 12 | `use_painted_buddy` | Saving keeps it; Use Character puts it on the real Buddy. Choose Use Character to apply it and head back. |
| 13 | `admire_painted_buddy` | Look at that. Genuine artistry — Buddy has never looked better. Wear it with pride. |
| 14 | `open_paint_background` | Buddy has a new look — the room deserves one too. Open Paint ▸ Background. |
| 15 | `select_background_spray` | Grab the Spray can this time. |
| 16 | `select_background_color` | Pick any colour you like from the palette. |
| 17 | `paint_background` | Now spray somewhere on the room behind Buddy. |
| 18 | `float_paint_background_panel` | The tool panel is in the way. Drag it by its title bar right out of the game window and park it off to the side — the 📌 button does the same. It becomes its own desktop window, and you get the whole background to admire. |
| 19 | `save_exit_paint_background` | Lovely. Choose Save and Exit to keep it. |
| 20 | `open_buddy_studio` | One more workshop to show you. Open Buddy Studio — this is where Buddy gets his hair, glasses, hats and everything else. |
| 21 | `select_nose_category` | Start with the Nose category. |
| 22 | `select_nose_button_style` | Click the Button nose once. A single click only previews it — watch it appear on Buddy in the preview window. |
| 23 | `buy_studio_item` | Like it? Buy it with the button on the right. Double-clicking the style itself does the same thing. |
| 24 | `equip_studio_item` | Now equip it. You can come back here any time to equip something else, or switch a slot back to its free default to take it off again. |
| 25 | `save_buddy_studio` | Save it, or Buddy loses his brand-new nose. |
| 26 | `exit_buddy_studio` | *(normal)* That is Buddy Studio. Exit when you are ready. <br>*(nose already worn)* Turns out he was already wearing that nose, so there was nothing to save — Save stays greyed out until something actually changes. That is Buddy Studio. Exit when you are ready. |
| 27 | `admire_studio_buddy` | Now that is a nose. Painted, kitted out, and frankly better dressed than most of us — Buddy is looking sharp. |
| 28 | `enter_work_mode` | Last stop. Work Mode shrinks Buddy into a tiny companion that sits on top of your real desktop and keeps earning while you get on with things. Open Work. |
| 29 | `drag_work_companion` | Hold the left mouse button on Buddy, on the computer, or on the blue bar, then move the mouse to drag the companion wherever you want it. |
| 30 | `resize_work_companion` | Hold the left mouse button on the ↘ resize button and move the mouse to make the companion bigger or smaller. |
| 31 | `toggle_work_counter` | That little screen counts what you have done. Click it to switch between this session and your lifetime total. |
| 32 | `exit_work_mode` | Done working? Double-click Buddy, or press X, to come back. |
| 33 | `farewell` | And that is everything. If you ever forget what something does, hit the ? button in the title bar and hover over it — Help mode will explain anything on screen. Go have fun with him. Goodbye! |

All 33 ids are prefixed `demo.onboarding.` in `TutorialStepIds`.

## Tool usage lines

Shown above the buy/equip text in the Inventory and Tools rows.
Source of truth: `ContentDisplayName.Usage` in `src/UI/ContentDisplayName.cs`.

| Tool | Text |
| --- | --- |
| Grab | Hold left mouse on Buddy to drag him, and let go while moving to fling him. |
| Power Grab | The same left-mouse drag as Grab, with a far stronger pull and a much harder throw. |
| Pet | Hold left mouse and stroke slowly over Buddy — he has a favourite spot. |
| Tickle | Hold left mouse and wiggle over Buddy; keep it up too long and he turns grumpy. |
| Baseball Bat | Hold right mouse to wind up through three charge stages, then let go to swing. |
| Boxing Glove | Swing the cursor into Buddy — the faster the glove moves, the harder it lands. |
| Baseball | Right mouse drops a ball at the cursor; grab it with left mouse, then hold right mouse and pull back to hurl it. |
| Soccer Ball | Right mouse drops the ball; grab it with left mouse, then hold right mouse and pull back to boot it across the room. |
| Meal | Right mouse drops a meal at the cursor; Buddy eats it where it lands, or grab it and pull back with right mouse to throw it at him. |
| Drink | Right mouse drops a drink at the cursor; Buddy takes it from there, or grab it and pull back with right mouse to throw it. |
| Repair Kit | Right mouse drops a kit; grab it with left mouse and throw it into Buddy to patch him back up. |
| Grenade | Right mouse drops a grenade; grab it with left mouse, then hold right mouse and pull back to lob it in an arc — pulling back also pulls the pin. |
| Nerf Blaster | Left mouse fires darts wherever the cursor points; press R to reload. |
| Pistol | Left mouse fires at the cursor; press R to reload when the magazine runs dry. |
| Shotgun | Left mouse fires a spread of pellets — brutal up close; press R to chamber the next shell. |
| Fire Sprayer | Hold left mouse to spray burning fuel; whatever it touches catches alight. |
