-------------------------------------------------------------------------------
--	hal.lua (snes9x)
--	~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
--	Provides a set of emulator-specific implementations of common connector
--	functions
-------------------------------------------------------------------------------

local base64 = require('base64')

local hal = { _version = "0.1.0" }

local function translateAddress(address, domain)
	if domain == "WRAM" then
		return 0x7e0000 + address
	elseif domain == "CARTRAM" then
		return 0x700000 + address
	elseif domain == "CARTROM" then
		return address
	end

	return address
end

function hal.read_u8(address, domain)
	return memory.readbyte(translateAddress(address, domain))
end

function hal.read_u16_le(address, domain)
	return memory.readword(translateAddress(address, domain))
end

function hal.read_u32_le(address, domain)
	return memory.readdword(translateAddress(address, domain))
end

function hal.write_u8(address, value, domain)
    memory.writebyte(translateAddress(address, domain), value)
end

function hal.write_u16_le(address, value, domain)
	memory.writeword(translateAddress(address, domain), value)
end

function hal.write_u32_le(address, value, domain)
	memory.writedword(translateAddress(address, domain), value)
end

--	Return a HAL-formatted byte-range read from the specified location
function hal.read_byte_range(address, length, domain)
	return memory.readbyterange(translateAddress(address, domain), length)
end

--	Write a HAL-formatted byte-range at the specified location
function hal.write_byte_range(address, byteRange, domain)
	for address, value in pairs(byteRange) do
		memory.writebyte(translateAddress(address, domain), value)
	end
end

--	Return a base64-encoded buffer from a HAL-formatted read_byte_range result
function hal.pack_byte_range(halByteBuffer, length)
	local result = ""
	for i = 1,length do
		result = result .. string.char(halByteBuffer[i])
	end
	return to_base64(result)
end

--	Return a HAL-appropriate byte-range from a base64-encoded buffer, for use with write_byte_range
function hal.unpack_byte_range(packedBuffer, offset)
    local unpacked = from_base64(packedBuffer)
    local result = {}
    for i=0, (string.len(unpacked)) do
        result[offset+i]=string.byte(unpacked, i+1)
    end
    return result
end

function hal.open_rom(path)
end

function hal.close_rom()
end

function hal.get_rom_path()
	return "Unsupported"
end

function hal.get_system_id()
	return "RAWSNES"
end

--	Displays a message on-screen in an emulator-defined way
function hal.message(msg)
	emu.message(msg)
	print(msg)
end

function hal.pause()
end

function hal.unpause()
end

local RenderList = { }

local function render_drawings()
	for idx, op in ipairs(RenderList) do
		op.func(op)
	end
end

function hal.draw_get_framebuffer_height()
	return 224
end

function hal.draw_begin()
	hal.draw_clear()
end

function hal.draw_end()
end

--	Render colored text at a specified pixel location
function hal.draw_text(x, y, msg, textColor, backColor)
	table.insert(RenderList, { x = x, y = y, text = msg, foreground = textColor, background = backColor,
		func = function (op) gui.text(op.x, op.y, op.text, op.foreground, op.background) end })
end

--	Clear the drawing canvas
function hal.draw_clear()
	RenderList = { }
end

function hal.framecount()
  return 0
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

local function onTick()
	invokeCallbackList(tickFuncs)
	render_drawings()
end

function hal.startup()
	--	Invoke startup callbacks
	invokeCallbackList(startupFuncs)

	emu.registerbefore(onTick)
end

function hal.shutdown()

	--	Invoke shutdown callbacks
	invokeCallbackList(shutdownFuncs)

	--	Clear callback lists
	startupFuncs = { }
	tickFuncs = { }
	shutdownFuncs = { }
end

function hal.validate_environment()
  return true
end

return hal