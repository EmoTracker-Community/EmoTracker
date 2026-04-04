require('class')

--	A simple finite state machine with tick and transition callbacks per state
StateMachine = class()

function StateMachine:init()
	self.States = { }
	self.CurrentStateName = nil
	self.CurrentState = nil
end

function StateMachine:register_state(name, onTick, onEnter, onExit)
	self.States[name] = { onTick = onTick, onEnter = onEnter, onExit = onExit }
end

function StateMachine:get_current_state_name()
	return self.CurrentStateName
end

function StateMachine:set_current_state(name)
	if name == self.CurrentStateName then
		return
	end

	if self.CurrentState then
		if self.CurrentState.onExit then
			self.CurrentState.onExit()
		end

		self.CurrentState = nil
	end

	self.CurrentState = self.States[name]

	if self.CurrentState then
		self.CurrentStateName = name

		if self.CurrentState.onEnter then
			self.CurrentState.onEnter()
		end
	end
end

function StateMachine:tick()
	if self.CurrentState then
		if self.CurrentState.onTick then
			self.CurrentState.onTick()
		end
	end
end