-- Smoke test pack init: exercises every concrete item type, accessibility
-- gating, and Lua scripting features.

print("Smoke pack init; variant: " .. tostring(Tracker.ActiveVariantUID))

-- Items of the standard JSON-declared types.
Tracker:AddItems("items/smoke_items.json")

-- Locations with accessibility rules keyed on the smoke codes above.
Tracker:AddLocations("locations/smoke_locations.json")

-- sectionchests item MUST be added after locations so its Section resolves.
Tracker:AddItems("items/smoke_items_extra.json")

-- Maps (for MapPanel layout element) + full layout tree exercising all element types.
Tracker:AddMaps("maps/maps.json")
Tracker:AddLayouts("layouts/smoke_layouts.json")

-- LuaItem: cannot be declared via a JSON type tag, so create it here.
SMOKE_LUA_CLICKS = 0
SMOKE_LUA_ON = false
SMOKE_LUA_RIGHT = false

local smokeLua = ScriptHost:CreateLuaItem()
smokeLua.Name = "Smoke Lua Item"
SMOKE_LUA_ITEM = smokeLua

smokeLua.OnLeftClickFunc = function(self)
    SMOKE_LUA_CLICKS = SMOKE_LUA_CLICKS + 1
    SMOKE_LUA_ON = not SMOKE_LUA_ON
    return true
end

smokeLua.OnRightClickFunc = function(self)
    SMOKE_LUA_CLICKS = SMOKE_LUA_CLICKS + 1
    SMOKE_LUA_RIGHT = true
    return true
end

smokeLua.CanProvideCodeFunc = function(self, code)
    return code == "smoke_lua"
end

smokeLua.ProvidesCodeFunc = function(self, code)
    if code == "smoke_lua" and SMOKE_LUA_ON then
        return 1
    end
    return 0
end

smokeLua.GetAllProvidedCodesFunc = function(self)
    return { "smoke_lua" }
end

print("Smoke pack init complete.")
