-------------------------------------------------------------------------------
--	hal.lua (Bizhawk)
--	~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
--	Provides a set of emulator-specific implementations of common connector
--	functions
-------------------------------------------------------------------------------

local base64 = require('base64')

local hal = { _version = "0.1.0" }

function hal.read_u8(address, domain)
	return memory.read_u8(address, domain)
end

function hal.read_u16_le(address, domain)
	return memory.read_u16_le(address, domain)
end

function hal.read_u32_le(address, domain)
	return memory.read_u32_le(address, domain)
end

function hal.write_u8(address, value, domain)
  memory.write_u8(address, value, domain)
end

function hal.write_u16_le(address, value, domain)
	memory.write_u16_le(address, value, domain)
end

function hal.write_u32_le(address, value, domain)
	memory.write_u32_le(address, value, domain)
end

--	Return a HAL-formatted byte-range read from the specified location
function hal.read_byte_range(address, length, domain)
	return memory.readbyterange(address, length, domain)
end

--	Write a HAL-formatted byte-range at the specified location
function hal.write_byte_range(address, byteRange, domain)
	memory.writebyterange(byteRange, domain)
end

--	Return a base64-encoded buffer from a HAL-formatted read_byte_range result
function hal.pack_byte_range(halByteBuffer, length)
	local result = ''
	for i = 0, length - 1 do
		result = result .. string.char(halByteBuffer[i])
	end
	return to_base64(result)
end

--	Return a HAL-appropriate byte-range from a base64-encoded buffer, for use with write_byte_range
function hal.unpack_byte_range(packedBuffer, offset)
  local unpacked = from_base64(packedBuffer)
  local result = {}
  --result:setn(unpacked:len())
  for i = 0, unpacked:len() do
    local n = i + 1
    result[offset + i] = unpacked:byte(n, n)
  end
  return result
end

function hal.open_rom(path)
	client.openrom(path)
end

function hal.close_rom()
	client.closerom()
end

function hal.get_rom_path()
	return gameinfo.getromname()
end

function hal.get_system_id()
	return emu.getsystemid()
end

--	Displays a message on-screen in an emulator-defined way
function hal.message(msg)
	gui.addmessage(msg)
	print(msg)
end

function hal.pause()
	client.pause()
end

function hal.unpause()
	client.unpause()
end

function hal.draw_get_framebuffer_height()
	return client.bufferheight()
end

function hal.draw_begin()
	gui.DrawNew("emu", true)
end

function hal.draw_end()
	gui.DrawFinish()
end

--	Render colored text at a specified pixel location
function hal.draw_text(x, y, msg, textColor, backColor)
	gui.pixelText(x, y, msg, textColor, backColor)
end

--	Clear the drawing canvas
function hal.draw_clear()
	gui.DrawNew("emu", true)
	gui.DrawFinish()
end

function hal.framecount()
  return emu.framecount()
end

local tickFuncs = { }
function hal.register_tick(name, callback)
	tickFuncs[name] = callback
end

function hal.unregister_tick(name)
	tickFuncs[name] = nil
end

local startupFuncs = { }
function hal.register_startup(name, callback)
	startupFuncs[name] = callback
end

local shutdownFuncs = { }
function hal.register_shutdown(name, callback)
	shutdownFuncs[name] = callback
end

function table.copy(t)
  local u = { }
  for k, v in pairs(t) do u[k] = v end
  return setmetatable(u, getmetatable(t))
end

local function invokeCallbackList(_callbacks)
	if next(_callbacks) then
		local callbacks = table.copy(_callbacks)
		for k, v in pairs(callbacks) do
			if v then
				v()
			end
		end
	end
end

function hal.shutdown()
	--	Invoke shutdown callbacks
	invokeCallbackList(shutdownFuncs)

	--	Clear callback lists
	startupFuncs = { }
	tickFuncs = { }
	shutdownFuncs = { }
end

function hal.startup()
	--	Clear any existing exit event registrations
	event.unregisterbyname('cc.exit')

	if emu.getluacore() ~= 'LuaInterface' then
		print('Unsupported Lua core:', emu.getluacore())
		return
	elseif emu.getsystemid() == 'NULL' then
		print('Emulator not running')
		-- Keep the script active with an empty loop
		-- It will reload after the emulator starts
		while true do emu.yield() end
	end

	client.unpause()
	event.onexit(hal.shutdown, 'cc.exit')

	--	Invoke startup callbacks
	invokeCallbackList(startupFuncs)

	while true do
		invokeCallbackList(tickFuncs)
		emu.yield()
	end
end

function hal.validate_environment()

  local bizhawk_version

  if client.getversion ~= nil then
    bizhawk_version = client.getversion()
  else 
  	if ((emu.getregisters().H == nil) or (emu.getregisters().H == 0)) then 
  		bizhawk_version = "2.3.0"
	else
		bizhawk_version = "unsupported" 
	end
  end

  if (emu.getluacore == nil) or ((bizhawk_version~="2.3.0") and (bizhawk_version~="2.3.1") and (bizhawk_version:sub(1,3)~="2.4")) then
    print("Crowd Control requires BizHawk 2.3.0, 2.3.1 or 2.4 to function.")
    print("You can download the correct version of BizHawk from:")
    print("        https://github.com/TASVideos/BizHawk/releases")
    print("Check our Setup Guide out at https://crowdcontrol.live/setup")
    print("If you need more help. Join our Discord! https://discord.warp.world")
    gui.text(25,50, "Crowd Control requires BizHawk 2.3 or 2.3.1 to function.")
    gui.text(25,100, "You can download the correct version of BizHawk from:")
    gui.text(25,150, "        https://github.com/TASVideos/BizHawk/releases")
    gui.text(25,200, "If you need more help. Join our Discord! https://discord.warp.world")
    return false
  elseif emu.getluacore() ~= 'LuaInterface' then
		print('Unsupported Lua core:', emu.getluacore())
    print("Crowd Control requires Lua+LuaInterface to function.")
    print("Click Config -> Customize and then the Advanced Tab.")
    print("At the bottom, click the Lua+LuaInterface and then OK.")
    print("Exit out of BizHawk completely and click Launch in CC.")
    print("Check our Setup Guide out at https://crowdcontrol.live/setup")
    gui.text(25,50, "Crowd Control requires Lua+LuaInterface to function.")
    gui.text(25,100, "Click Config -> Customize and then the Advanced Tab.")
    gui.text(25,150, "At the bottom, click the Lua+LuaInterface and then OK.")
    gui.text(25,200, "Exit out of BizHawk completely and click Launch in CC.")
    gui.text(25,250, "If you need more help. Join our Discord! https://discord.warp.world")
		return false
	end
  return true
end

return hal