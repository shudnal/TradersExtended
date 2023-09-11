# Traders Extended
![logo](https://staticdelivery.nexusmods.com/mods/3667/images/headers/2509_1694134634.jpg)

Trader specific buy and sell lists extended. Store UI extended. Sellable items listed next to tradeable with option to sell exact item.

# Description

Yet another trader mod.

Less bloated then BetterTrader if your needs are much smaller than economics simulating.

A bit more functional than another "simple" trader mods.

## Features
* extended store UI with item to sell list
* trader specific or common configurable lists of additional items to buy/sell
* live update on config changes
* server synced config
* configs saved as JSON files next to dll or in subdirectories
* configurable items are added to current lists (not replacing current vanilla items)
* double click on stackable item you want to buy (i.g. Iron pit ) to enter needed items amount

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
Extract TradersExtended.dll file to your BepInEx\Plugins\TradersExtended\ folder

Create new config file next to dll to add items.

## Compatibility
* The mod should be compatible with any mods changing item prices to make it sellable and extending tradeable item lists
* Incompatible with any mod hiding vanilla store UI
* Mod should be compatible with mods adding more traders with unique names (until they use vanilla store UI)

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2509)

[Thunderstore](https://valheim.thunderstore.io/package/shudnal/TradersExtended/)

## Changelog

v 1.0.2
* double click on stackable item to input needed amount
* item config unified

v 1.0.1
* option to load config stored internally

v 1.0.0
* Initial release