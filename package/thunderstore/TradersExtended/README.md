# Traders Extended
![logo](https://staticdelivery.nexusmods.com/mods/3667/images/headers/2509_1710675587.jpg)

Trader specific buy and sell items lists. Store UI with sell list, filter and repair button. Gamepad support. Trader use coins. Markups and discounts.

## Features
* extended store UI with item to sell list
* trader specific or common configurable lists of additional items to buy/sell
* live update on config changes
* server synced config
* configs saved as JSON files 
* config could be stored next to dll, in config folder in any subdirectory or even be embedded into dll
* configurable items are added to current lists (not replacing current vanilla items)
* double click on stackable item you want to buy to enter needed items amount
* trader could repair your armor or weapons for coins (by default Haldor repair weapons and Hildir repair armor)
* trader could have limited replenished amount of coins
* trader could give a discount or set a markup depending on current amount of coins
* you can filter both buy and sell list by item name
* you can customize coins weight and exact stack size
* EpicLoot support (colored icons and coins spent in Adventure mode)
* you can buyback last sold item (item will be available until relog)
* items from hotbar, quick slots and equipped armor will not appear in sell list

## Check for discovery

Traders will wait for item discovery before it appears in the store.

To exclude some item from that rule you can set its prefab name in "Undiscovered items list to sell" config.

Vanilla items will be available (if its addition is not disabled and global key met).

If you disable automatic addition of vanilla item in trader list you have to manually add items in buy list and add its prefab names to "Undiscovered items list to sell" config.

## Repair

Trader will repair your items for 2 coins each. You can set cost in coins.

Set trader name or its prefab name in the appropriate list for it to repair set type of items. Weapons or Armor.

## Buyback

If you have recently sold an item to a Trader it will be available to buy back at first position on the buy list and will be color highlighted.

Colors are configurable.

Only last sold item is available to buy back.

## Trader use coins

You can enable traders to use limited amount of coins. When you sell item trader will spend its coins. When you buy item trader will receive your coins.

Every morning (at set replenishment rate) every trader will be given settable amount of coins until it have set maximum amount of coins.

If trader have more coins than minimum amount - sell price will be raised and buy price reduced. If trader have less coins than minimum - sell price will be reduced and buy prices increased.

If you want custom traders to operate coins you must set its prefab name in "Custom traders prefab names" config. Case sensitive, comma separated.

If you're host(or use server devcommands) and admin you can use console command ```settradercoins [Trader name] [amount]``` to manually set trader coins.

Defaults:
* trader have 2000 coins
* trader will replenish 1000 coins every morning until it have 6000 coins
* if trader have 0 coins markup for buy prices will be +50% and sell prices -30%
* if trader have 6000 coins discount for buy prices will be -30% and sell prices will be increased to +50%
* normal prices will be at 2000 coins and will gradually change

## Gamepad support
* use left gamepad stick to scroll buy list
* use right gamepad stick to scroll sell list
* use DPad to operate both lists
* Use (X) for XBox, (☐) for PS to sell selected item
* hold Alternate action and press (A) for XBox, (X) for PS to open items amount dialog
* use Right Stick click to repair item

## Config file names
* are case insensitive for Windows
* should start with mod ID "shudnal.TradersExtended" and should have a ".json" extension (case sensitive for \*nix)
* should include trader name (or "common") and list type (buy/sell)
* can be set for nonstandard trader name (both m_name and prefab name work)

Config file names for example:
* shudnal.TradersExtended.haldor.buy.json (items to buy from Haldor)
* shudnal.TradersExtended.haldor.sell.json (items to sell to Haldor)
* shudnal.TradersExtended.hildir.buy.json (items to buy from Hildir)
* shudnal.TradersExtended.hildir.sell.json (items to sell to Hildir)
* shudnal.TradersExtended.bogwitch.buy.json (items to sell to Bog Witch)
* shudnal.TradersExtended.bogwitch.buy.json (items to sell to Bog Witch)
* shudnal.TradersExtended.common.buy.json (items to buy from all traders)
* shudnal.TradersExtended.common.sell.json (items to sell to all traders)

## Config formatting

Configs are JSON files containing array of objects with different formats for buy and sell lists.

Configs use game object prefab name. Prefab name is case sensitive. Current list of items [here](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html)

For example "Simple cap red" sold by Hildir will be "HelmetHat5". Incorrect prefab names will be safely ignored.

Configs use Boss Keys to filter tradeable item list (https://valheim.fandom.com/wiki/Global_Keys).

You can use console command ```tradersextended save``` It will generate ObjectDB.list.json file in **\BepInEx\config\shudnal.TradersExtended\** folder with all the items currently in your game.

### Google Sheets JSON Helper

For easy configs formatting you can use [Google Sheets JSON Helper](https://docs.google.com/spreadsheets/d/1VgGlERaRb2rDB6ULdoWM39Sh0L_X1sR4dJE1CgRDsYI). It was created for this mod to help editing configs.

More info in the spreadsheet itself.

Credits for the idea, first implementation, testing and documentation for @MeowingInsanely!

### Tradeable item model

```json
{
  "prefab": "PrefabName", 
  "stack": 1,
  "price": 100,
  "requiredGlobalKey": "",
  "notRequiredGlobalKey": "",
  "requiredPlayerKey": "",
  "notRequiredPlayerKey": "",
}
```

* prefab - string - Prefab name of item. Column Item from [item list](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html)
* stack - integer - how many items in stack. If set to 0 then item will be ignored.
* price - integer - price for stack. If set to 0 then item will be ignored.
* quality - integer - quality of item. If set to 0 then for buy list quality will be default and for sell list quality will not be checked
* requiredGlobalKey - string, comma-separated - if set, then all global keys from the list should be set for item to appear. In other words if any global key is not set then item will not be available.
* notRequiredGlobalKey - string, comma-separated - if set, then all global keys from the list should NOT be set for item to appear. In other words if any global key is set then item will not be available.
* requiredPlayerKey - string, comma-separated - if set, then all player unique keys from the list should be set for item to appear. In other words if any player unique key is not set then item will not be available.
* notRequiredPlayerKey - string, comma-separated - if set, then all player unique keys from the list should NOT be set for item to appear. In other words if any player unique key is set then item will not be available.

### Tradeable(Buy) list example
* I want to be able to buy a Dragon egg for 500 coins after I had killed Moder
* I want to be able to buy a Boar meat for 10 coins
* I want to be able to buy an Ancient Seed until Elder is killed
* I want to be able to buy max quality Cultivator after I had Elder killed
```json
[
  {
    "prefab": "DragonEgg", 
    "stack": 1,
    "price": 500,
    "requiredGlobalKey": "defeated_dragon"
  },
  {
    "prefab": "RawMeat", 
    "stack": 1,
    "price": 10,
  },  
  {
    "prefab": "AncientSeed", 
    "stack": 1,
    "price": 1000,
    "notRequiredGlobalKey": "defeated_gdking"
  },
  {
    "prefab": "Cultivator", 
    "stack": 1,
    "price": 500,
    "requiredGlobalKey": "defeated_gdking",
    "quality": 4,
  },
]
```

### Sellable(Sell) list example
* I want to be able to sell a Fishing rod for 200 coins
* I want to be able to sell a stack of Wood for 25 coins after Elder was killed
* I want to be able to sell Perch with 50 gold each but also sell x5 Perch of quality 4 for more.
```json
[
  {
    "prefab": "FishingRod", 
    "stack": 1,
    "price": 200,
  },
  {
    "prefab": "Wood", 
    "stack": 50,
    "price": 25,
    "requiredGlobalKey": "defeated_gdking"
  },
  {
    "prefab": "Fish1",
    "stack": 1,
    "price": 50,
  },
  {
    "prefab": "Fish1",
    "stack": 5,
    "price": 500,
    "quality": 4,
  },
]
```

## Installation (manual)
Copy TradersExtended folder to your BepInEx\Plugins\ folder

Create new config file next to dll or in BepInEx\Config\ folder to add items.

## Compatibility
* The mod should be compatible with any mods changing item prices to make it sellable and extending tradeable item lists
* Incompatible with any mod altering vanilla store UI (straight incompatible with AUGA)
* The mod should be compatible with mods adding more traders with unique names (until they use vanilla store UI)
* The mod should be compatible with mods adding more items to store (until its patches are noninvasive)

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

For raw JSON editing [https://jsoneditoronline.org/](https://jsoneditoronline.org/).

Tool for that mod [Traders Extended - JSON Helper](https://docs.google.com/spreadsheets/d/1VgGlERaRb2rDB6ULdoWM39Sh0L_X1sR4dJE1CgRDsYI).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2509)

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)