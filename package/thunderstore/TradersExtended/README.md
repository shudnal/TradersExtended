# Traders Extended
![logo](https://staticdelivery.nexusmods.com/mods/3667/images/headers/2509_1710675587.jpg)

Trader specific buy and sell lists extended. Store UI extended. Sellable items listed next to tradeable with option to sell exact item.

# Description

Yet another trader mod.

Less bloated then BetterTrader if your needs are smaller than complete economics simulating.

A bit more functional than another "simple" trader mods.

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

## Check for discovery

Traders will wait for item discovery before it appears in the store.

To exclude some item from that rule you can set its prefab name in "Undiscovered items list to sell" config.

Vanilla items will always be there (if global key met).

## Repair

Trader will repair your items for 2 coins each. You can set cost in coins.

Set trader name or its prefab name in the appropriate list for it to repair set type of items. Weapons or Armor.

## Trader use coins

You can enable traders to use limited amount of coins. When you sell item trader will spend its coins. When you buy item trader will receive your coins.

Every morning (at set replenishment rate) every trader will be given settable amount of coins until it have set maximum amount of coins.

If trader have more coins than minimum amount - sell price will be raised and buy price reduced. If trader have less coins than minimum - sell price will be reduced and buy prices increased.

If you want custom traders to operate coins you must set its prefab name in "Custom traders prefab names" config. Case sensitive, comma separated.

Defaults:
* trader have 2000 coins
* trader will replenish 1000 coins every morning until it have 6000 coins
* if trader have 0 coins markup for buy prices will be +50% and sell prices -30%
* if trader have 6000 coins discount for buy prices will be -30% and sell prices will be increased to +50%
* normal prices will be at 2000 coins and will gradually change

## Gamepad support
* use right gamepad stick to scroll sell list
* hold Left trigger to scroll sell list using DPad up and down
* Use (X) for XBox, (☐) for PS to sell selected item
* hold Left trigger and press (A) for XBox, (X) for PS to open items amount dialog
* use Right Stick click to repair item

## Config file names
* are case insensitive for Windows
* should start with mod ID "shudnal.TradersExtended" and should have a ".json" extension (case sensitive for \*nix)
* should include trader name (or "common") and list type (buy/sell)
* can be set for nonstandard trader name

Config file names for example:
* shudnal.TradersExtended.haldor.buy.json (items to buy from haldor (ingame name $npc_haldor))
* shudnal.TradersExtended.haldor.sell.json (items to sell to haldor (ingame name $npc_haldor))
* shudnal.TradersExtended.hildir.buy.json (items to buy from hildir (ingame name $npc_hildir))
* shudnal.TradersExtended.hildir.sell.json (items to sell to hildir (ingame name $npc_hildir))
* shudnal.TradersExtended.common.buy.json (items to buy from all traders)
* shudnal.TradersExtended.common.sell.json (items to sell to all traders)

## Config formatting

Configs are JSON files containing array of objects with different formats for buy and sell lists.

Configs use game object prefab name. Current list of items [here](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html)

For example "Simple cap red" sold by Hildir will be "HelmetHat5". Wrongly set prefab names will be ignored.

Configs use Boss Keys to filter tradeable item list (https://valheim.fandom.com/wiki/Global_Keys).

### Tradeable(Buy) list example
* I want to be able to buy a Dragon egg for 500 coins after I had killed Moder
* I want to be able to buy a Boar meat for 10 coins
```
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
    "requiredGlobalKey": ""
  },  
]
```

### Sellable(Sell) list example
I want to be able to sell a Fishing rod for 200 coins
I want to be able to sell a stack of Wood for 25 coins after Elder was killed
```
[
  {
    "prefab": "FishingRod", 
    "stack": 1,
    "price": 200,
    "requiredGlobalKey": ""
  },
  {
    "prefab": "Wood", 
    "stack": 50,
    "price": 25,
    "requiredGlobalKey": "defeated_gdking"
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

For JSON editing [https://jsoneditoronline.org/](https://jsoneditoronline.org/).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2509)