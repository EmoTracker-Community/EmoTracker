
local hal = require('hal')
if not hal.validate_environment() then
  hal.pause()
  return
end

local VERSION_MAJOR = 2
local VERSION_MINOR = 2
local VERSION_PATCH = 0
local VERSION = VERSION_MAJOR .. '.' .. VERSION_MINOR .. '.' .. VERSION_PATCH

local json = require('json')
local socket = require('socket.core')

local HOST_ADDRESS = '127.0.0.1'
local HOST_PORT = 43884
local RECONNECT_INTERVAL = 3
local KEEPALIVE_DELAY = 5

local tcp = nil
local currTime = 0
local lastTime = 0
local receiveSize = 0
local receivePart = nil

local busDomain = nil
local effectQueue = {}
local memFreezes = {}

require('statemachine')
local connectorStateMachine = StateMachine()

--	State names for connectorStateMachine
local STATE_CONNECTING = "connecting"
local STATE_CONNECTED = "connected"
local STATE_EXIT = "exit"

function applyFreezes()
  for k, fz in pairs(memFreezes) do
    local sz = fz['size']
    if checkCond(fz) then
      local wType = fz['writeType']
      local val
      local rF, wF
      if (sz == 1) and (wType > 0) then
        rF = hal.read_u8
        wF = hal.write_u8
      elseif sz == 2 then
        rF = hal.read_u16_le
        wF = hal.write_u16_le
      elseif sz == 4 then
        rF = hal.read_u32_le
        wF = hal.write_u32_le
      end
      
      if (fz['writeType'] == 0x01) then
        val = fz['value']
      elseif (fz['writeType'] == 0x02) then
        val = rF(fz['address'], fz['domain']) + fz['value']
      elseif (fz['writeType'] == 0x03) then
        val = rF(fz['address'], fz['domain']) - fz['value']
      elseif (fz['writeType'] == 0x04) then
        val = bit.bor(bit.band(fz['value'], fz['mask']), bit.band(rF(fz['address'], fz['domain']), bit.bnot(fz['mask'])))
      end
      
      wF(fz['address'], val, fz['domain'])
    end
  end
end

function checkCond(fz)
  local cond = fz['condition']
  if cond == 0x03 then --always
    return true;
  end
  
  local result
  local size = fz['cmpSize']
  
  if bit.band(cond, 0x80) == 0x80 then
    result = hal.framecount()
    cond = bit.band(cond, 0x0F)
  else
    if size == 1 then
      result = hal.read_u8(fz['cmpAddress'], fz['domain'])
    elseif cond == 2 then
      result = hal.read_u16_le(fz['cmpAddress'], fz['domain'])
    elseif cond == 4 then
      result = hal.read_u32_le(fz['cmpAddress'], fz['domain'])
    end
  end
  
  if cond == 0x01 then --equal
    return result == fz['cmpValue']
  elseif cond == 0x02 then --not equal
    return result ~= fz['cmpValue']
  elseif cond == 0x04 then --greater than
    return result > fz['cmpValue']
  elseif cond == 0x05 then --greater than or equal
    return result >= fz['cmpValue']
  elseif cond == 0x08 then --less than
    return result < fz['cmpValue']
  elseif cond == 0x09 then --less than or equal
    return result <= fz['cmpValue']
  elseif cond == 0x11 then --mask set
    return bit.band(fz['cmpValue'],result) == fz['cmpValue']
  elseif cond == 0x12 then --mask unset
    return bit.band(fz['cmpValue'],result) ~= fz['cmpValue']
  end
  return false
end

local function removeHold(addr)
  for i, v in pairs(memFreezes) do 
    if (v.address == addr) then
      memFreezes[i] = nil
    end
  end
end

function processBlock(block)
  local commandType = block['type']
  local domain = block['domain']
  local address = block['address']
  local value = block['value']
  local size = block['size']

  local result = {
    id = block['id'],
    stamp = os.time(),
    type = commandType,
    message = '',
    address = address,
    size = size,
    domain = domain,
    value = value
  }

  if commandType == 0x00 then --read byte
    result['value'] = hal.read_u8(address, domain)
  elseif commandType == 0x01 then --read ushort
    result['value'] = hal.read_u16_le(address, domain)
  elseif commandType == 0x02 then --read uint
    result['value'] = hal.read_u32_le(address, domain)
  elseif commandType == 0x0F then --read block
    result['block'] = hal.pack_byte_range(hal.read_byte_range(address, value, domain), value)
  elseif commandType == 0x10 then --write byte
    hal.write_u8(address, value, domain)
    if memFreezes[address] ~= nil then memFreezes[address]['value'] = value end
  elseif commandType == 0x11 then --write ushort
    hal.write_u16_le(address, value, domain)
    if memFreezes[address] ~= nil then memFreezes[address]['value'] = value end
  elseif commandType == 0x12 then --write uint
    hal.write_u32_le(address, value, domain)
    if memFreezes[address] ~= nil then memFreezes[address]['value'] = value end
  elseif commandType == 0x1F then --write block
    local m = hal.unpack_byte_range(block['block'], address)
    hal.write_byte_range(address, m, domain)
  elseif commandType == 0x20 then --safe bit flip (atomic)
    local old = hal.read_u8(address, domain)
    hal.write_u8(address, bit.bor(old, value), domain)
    block['value'] = old
  elseif commandType == 0x21 then --safe bit unflip (atomic)
    local old = hal.read_u8(block['address'])
    hal.write_u8(address, bit.band(old, bit.bnot(value)), domain)
    block['value'] = old
  elseif commandType == 0x30 then --memory freeze unsigned
    table.insert(memFreezes, {
      address = address,
      domain = domain,
      value = value,
      size = size,
      mask = 0xFF,
      writeType = block['writeType'],
      cmpAddress = block['cmpAddress'],
      cmpValue = block['cmpValue'],
      cmpSize = block['cmpSize'],
      condition = block['condition']
    })
  elseif commandType == 0x3F then --memory unfreeze
    removeHold(address)
  elseif commandType == 0xE0 then --load rom
    hal.open_rom(block['message'])
  elseif commandType == 0xE1 then --unload rom
    hal.close_rom()
  elseif commandType == 0xE2 then --get rom path
    result['message'] = hal.get_rom_path()
  elseif commandType == 0xE3 then --get emulator core id
    local a = bit.band(bit.rshift(value, 16), 0xFF)
    local b = bit.band(bit.rshift(value, 8), 0xFF)
    local c = bit.band(value, 0xFF)
    -- print('Server version ' .. a .. '.' .. b .. '.' .. c)

    local major = bit.lshift(VERSION_MAJOR, 16)
    local minor = bit.lshift(VERSION_MINOR, 8)
    result['value'] = bit.bor(major, bit.bor(minor, VERSION_PATCH))
    result['message'] = hal.get_system_id()
  elseif commandType == 0xF0 then
    hal.message(block['message'])
  elseif commandType == 0xFF then
    -- do nothing
  else
    print('Unknown block type received:', commandType)
    return
  end

  sendBlock(result)
end

function sendBlock(block)
  local data = json.encode(block)
  local size = data:len()
  -- print('send', data)

  local a = string.char(bit.band(bit.rshift(size, 24), 0xFF))
  local b = string.char(bit.band(bit.rshift(size, 16), 0xFF))
  local c = string.char(bit.band(bit.rshift(size, 8), 0xFF))
  local d = string.char(bit.band(size, 0xFF))

  local ret, err = tcp:send(a .. b .. c .. d .. data)
  if ret == nil then
    print('Failed to send:', err)
  end
end

function receiveData(n)
  local data, err, part = tcp:receive(n, receivePart)
  if data == nil then
    if err ~= 'timeout' then
      print('Connection lost:', err)
      reconnect()
    else
      receivePart = part
    end
  else
    receivePart = nil
  end
  return data
end

function receive()
  currTime = os.time()

  while true do
    if receiveSize == 0 then
      local n = receiveData(4)
      if n == nil then break end

      local n1, n2, n3, d = n:byte(1, 4)
      local a = bit.lshift(n1, 24)
      local b = bit.lshift(n2, 16)
      local c = bit.lshift(n3, 8)
      receiveSize = bit.bor(a, bit.bor(b, bit.bor(c, d)))
    end

    if receiveSize ~= 0 then
      local data = receiveData(receiveSize)
      if data == nil then break end

      -- print('recv', data)
      processBlock(json.decode(data))
      receiveSize = 0
    end

    lastTime = currTime
  end

  if lastTime + KEEPALIVE_DELAY < currTime then
    print('Keepalive failed')
    reconnect()
  end
end

function reconnect()
	if connectorStateMachine:get_current_state_name() ~= STATE_EXIT then
		connectorStateMachine:set_current_state(STATE_CONNECTING)
	end
end

function disconnect()
	if tcp then
		tcp:shutdown()
		tcp:close()
		tcp = nil
	end
end

--	Connector State Machine Implementation

local function onEnter_Connecting()
	hal.draw_begin()
	local y = hal.draw_get_framebuffer_height() / 2
	hal.draw_text(2, y, 'Connecting to ConnectorLib host...', 'red', 'black')
	hal.draw_end()

	hal.pause()
end

local function onExit_Connecting()
	hal.draw_clear()
	hal.unpause()	
end

local function onTick_Connecting()
    currTime = os.time()
    if lastTime + RECONNECT_INTERVAL <= currTime then
		lastTime = currTime
		tcp = socket.tcp()

		local ret, err = tcp:connect(HOST_ADDRESS, HOST_PORT)
		if ret == 1 then
			hal.message('Connection established')
			tcp:settimeout(0)

			connectorStateMachine:set_current_state(STATE_CONNECTED)
		else
			print('Failed to open socket:', err)
			tcp:close()
			tcp = nil
		end
    end
end

local function onTick_Connected()
    receive()
    applyFreezes()
end

local function onExit_Connected()
	disconnect()
end

local function onEnter_Exit()
	disconnect()
end

local function tick()
	connectorStateMachine:tick()
end

local function shutdown()
	connectorStateMachine:set_current_state(STATE_EXIT)
end

connectorStateMachine:register_state(STATE_CONNECTING, onTick_Connecting, onEnter_Connecting, onExit_Connecting)
connectorStateMachine:register_state(STATE_CONNECTED, onTick_Connected, nil, onExit_Connected)
connectorStateMachine:register_state(STATE_EXIT, nil, onEnter_Exit, nil)

local function startup()
	connectorStateMachine:set_current_state(STATE_CONNECTING)	
end

print('ConnectorLib Lua Connector ' .. VERSION .. ' (' .. socket._VERSION .. ')')

--	Configure and startup the HAL	
hal.register_startup("connector_startup", startup)
hal.register_tick("connector_tick", tick)
hal.register_shutdown("connector_shutdown", shutdown)
hal.startup()
