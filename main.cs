function SimObject::processInputEvent(%obj, %EventName, %client)
{
	if (%obj.numEvents <= 0)
		return;

	%foundOne = false;
	for(%i = 0; %i < %obj.numEvents; %i++) //is there an input event for this %eventName
	{
		if (%obj.eventInput[%i] $= %EventName && %obj.eventEnabled[%i])
		{
			%foundOne = true;
			break;
		}
	}

	if (!%foundOne)
		return;

	if (isObject (%client))
		%quotaObject = getQuotaObjectFromClient (%client);
	else if (%obj.getType () & $TypeMasks::FxBrickAlwaysObjectType)
		%quotaObject = getQuotaObjectFromBrick (%obj);
	else 
	{
		if(getBuildString () !$= "Ship")
			error ("ERROR: SimObject::ProcessInputEvent() - could not get quota object for event \"" @ %EventName @ "\" on object " @ %obj);

		return;
	}

	if(!isObject (%quotaObject))
		error ("ERROR: SimObject::ProcessInputEvent() - new quota object creation failed!");

	setCurrentQuotaObject (%quotaObject);
	if (%EventName $= "OnRelay" && %obj.implicitCancelEvents) //implicitCancelEvents - prevents double relay from going out of control
		%obj.cancelEvents();

	for(%i = 0; %i < %obj.numEvents; %i++)
	{
		//enabled & delay = 0 && eventLine# == CancelEvents
		if (%obj.eventEnabled[%i] && %obj.eventInput[%i] $= %EventName && %obj.eventOutput[%i] $= "CancelEvents" && %obj.eventDelay[%i] == 0)
		{
			if (%obj.eventTarget[%i] == -1)
			{
				%name = %obj.eventNT[%i];
				%group = %obj.getGroup();
				for(%j = 0; %j < %group.NTObjectCount[%name]; %j++)
				{
					%target = %group.NTObject[%name, %j];
					if(isObject(%target))
						%target.cancelEvents();
				}
			}
			else
			{
				%target = $InputTarget_[%obj.eventTarget[%i]];
				if(isObject(%target))
					%target.cancelEvents();
			}
		}
	}

	%eventCount = 0;
	for(%i = 0; %i < %obj.numEvents; %i++)
	{
		//if (%obj.eventInput[%i] $= %EventName && %obj.eventEnabled[%i] && %obj.eventOutput[%i] !$= "CancelEvents" && %obj.eventDelay[%i] > 0)

		//decompiler did scary things so i just added a continue - https://github.com/Electrk/bl-decompiled/blob/master/server/scripts/allGameScripts.cs#L241
		if (%obj.eventInput[%i] !$= %EventName || !%obj.eventEnabled[%i] || %obj.eventOutput[%i] $= "CancelEvents" && %obj.eventDelay[%i] == 0)
			continue;

		if (%obj.eventTarget[%i] == -1)
		{
			%name = %obj.eventNT[%i];
			%group = %obj.getGroup();
			for(%j = 0; %j < %group.NTObjectCount[%name]; %j++)
			{
				%target = %group.NTObject[%name, %j];
				if(isObject(%target))
					%eventCount++;
			}
		}
		else 
		{
			%eventCount++;
		}
	}

	if (%eventCount == 0)
		return;

	%currTime = getSimTime ();
	if(%eventCount > %quotaObject.getAllocs_Schedules())
	{
		commandToClient (%client, 'CenterPrint', "<color:FFFFFF>Too many events at once!\n(" @ %EventName @ ")", 1);
		if (%client.SQH_StartTime <= 0)
			%client.SQH_StartTime = %currTime;
		else 
		{
			if(%currTime - %client.SQH_LastTime < 2000)
				%client.SQH_HitCount += 1;

			if(%client.SQH_HitCount > 5)
			{
				%client.ClearEventSchedules();
				%client.resetVehicles();
				%mask = $TypeMasks::PlayerObjectType | $TypeMasks::ProjectileObjectType | $TypeMasks::VehicleObjectType | $TypeMasks::CorpseObjectType;
				%client.ClearEventObjects(%mask);
			}
		}

		%client.SQH_LastTime = %currTime;
		return;
	}

	if (%currTime - %client.SQH_LastTime > 1000)
	{
		%client.SQH_StartTime = 0;
		%client.SQH_HitCount = 0;
	}

	for(%i = 0; %i < %obj.numEvents; %i++)
	{
		//decompiler did scary things so i just added a continue - https://github.com/Electrk/bl-decompiled/blob/master/server/scripts/allGameScripts.cs#L315
		if (%obj.eventInput[%i] !$= %EventName || !%obj.eventEnabled[%i] || %obj.eventOutput[%i] $= "CancelEvents" && %obj.eventDelay[%i] == 0)
			continue;

		%eventStack[%eventStackCount + 0] = %i;
		%eventStackCount++;
	}

	if(%eventStackCount == 0)
		return;
	
	EventManager_pushStack();

	for(%i = %eventStackCount - 1; %i >= 0; %i--) //push the data on so the first event to be run is on the top (a stack)
	{
		%line = %eventStack[%i];
		%target = %obj.eventTarget[%line];
		if(%target != -1)
			%target = $InputTarget_[%target];

		EventManager_pushStackData(%obj, %target, %eventName, %client, %line);
	}

	EventManager_popStack();
}

function EventManager_pushStack()
{
	%currStack = $EventManager_CurrMainStack;
	if(%currStack == -1 || $EventManager_MainStackCount == 0)
	{
		//register a new main stack
		for(%currStack = 0; %currStack < $EventManager_MainStackCount; %currStack++)
			if($EventManager_MainStack[%currStack, VALID] == false)
				break;
		
		$EventManager_MainStack[%currStack, COUNT] = 0;
		$EventManager_MainStack[%currStack, DEPTH] = 0;
		$EventManager_MainStack[%currStack, VALID] = true;

		$EventManager_CurrMainStack = %currStack;
		$EventManager_MainStackCount++;
	}

	//create a new sub-stack
	%SubIndex = $EventManager_MainStack[%currStack, COUNT];

	$EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT] = 0;

	$EventManager_MainStack[%currStack, COUNT]++;
	$EventManager_MainStack[%currStack, DEPTH]++; //we can decrement this value in popStack
}

function EventManager_popStack()
{
	%currStack = $EventManager_CurrMainStack;
	%depth = $EventManager_MainStack[%currStack, DEPTH]--;

	//exit the current main-stack
	if(%depth == 0)
		$EventManager_CurrMainStack = -1;
}

function EventManager_pushStackData(%obj, %target, %eventName, %client, %i)
{
	//push to a sub stack
	%currStack = $EventManager_CurrMainStack;

	//peek the top of the stack
	%SubIndex = $EventManager_MainStack[%currStack, COUNT] - 1;

	%eventCount = $EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT] + 0;

	$EventManager_MainStack[%currStack, %SubIndex, EVENT_OBJ   , %eventCount] = %obj;
	$EventManager_MainStack[%currStack, %SubIndex, EVENT_TARGET, %eventCount] = %target;
	$EventManager_MainStack[%currStack, %SubIndex, EVENT_NAME  , %eventCount] = %eventName;
	$EventManager_MainStack[%currStack, %SubIndex, EVENT_CLIENT, %eventCount] = %client;
	$EventManager_MainStack[%currStack, %SubIndex, EVENT_LINE  , %eventCount] = %i;

	//increase the event count
	$EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT]++;

	//talk("newEvent["@ %eventCount @"] ("@ %eventName @") / "@ %obj.eventOutput[%i]);
}


function EventManager_ProcessEvent(%currStack)
{
	//peek the top of the stack
	%SubIndex = $EventManager_MainStack[%currStack, COUNT] - 1;

	//the top event
	%eventIndex = $EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT] - 1;

	%obj 		= $EventManager_MainStack[%currStack, %SubIndex, EVENT_OBJ   , %eventIndex];
	%target 	= $EventManager_MainStack[%currStack, %SubIndex, EVENT_TARGET, %eventIndex];
	%eventName  = $EventManager_MainStack[%currStack, %SubIndex, EVENT_NAME  , %eventIndex];
	%client 	= $EventManager_MainStack[%currStack, %SubIndex, EVENT_CLIENT, %eventIndex];
	%line 		= $EventManager_MainStack[%currStack, %SubIndex, EVENT_LINE  , %eventIndex];

	$EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT]--;

	//if it has no events left this sub stack is useless
	if($EventManager_MainStack[%currStack, %SubIndex, EVENT_COUNT] == 0)
	{
		//delete sub stack data
		deleteVariables("$EventManager_MainStack" @ %currStack @ "_"  @ %SubIndex @ "_*");

		$EventManager_MainStack[%currStack, COUNT]--;

		//talk("DELETED SUB-STACK");
	}

	if(!isObject(%obj) || !isObject(%client))
	{
		//talk("%obj OR %client is NOT AN OBJECT (" @ isObject(%obj) SPC isObject(%client) @ ")");
		return 0;
	}


	//TODO?: SAVE ALL $InputTarget_[ CLASSES ]
	if(%eventName !$= %obj.eventInput[%line])
	{
		//talk("%eventName is different from %obj.eventInput[%line] ("@ %eventName @" / "@ %obj.eventInput[%line] @")");
		return 0;
	}
	// SimObject::EM_parseEventNumParameters(%target, %obj, %i, %numParameters, %client)

	%outputEventIdx = %obj.eventOutputIdx[%line];
	if(%target == -1) //do brick stuff
	{
		%name = %obj.eventNT[%line];
		%group = %obj.getGroup();
		for(%j = 0; %j < %group.NTObjectCount[%name]; %j++)
		{
			%target = %group.NTObject[%name, %j];
			if(!isObject(%target))
				continue;

			%targetClass = "fxDTSBrick";
			%numParameters = outputEvent_GetNumParametersFromIdx(%targetClass, %outputEventIdx);

			%delay = %target.eventDelay[%line];
			if(%delay == 0)
				%target.EM_parseEventNumParameters(%obj, %line, %numParameters, %client);
			else
				%target.parseEventNumParameters(%obj, %line, %numParameters, %delay, %client);

			%callCount++;
		}
	} else { //non-brick targets
		if(!isObject(%target))
		{
			//talk("%TARGET IS NOT AN OBJECT");
			return 0;
		}

		%targetClass = inputEvent_GetTargetClass ("fxDTSBrick", %obj.eventInputIdx[%line], %obj.eventTargetIdx[%line]);
		%numParameters = outputEvent_GetNumParametersFromIdx(%targetClass, %outputEventIdx);

		//talk(%obj.eventOutput[%line] SPC %numParameters);
		%delay = %target.eventDelay[%line];
		if(%delay == 0)
			%target.EM_parseEventNumParameters(%obj, %line, %numParameters, %client);
		else 
			%target.parseEventNumParameters(%obj, %line, %numParameters, %delay, %client);

		%callCount++;
	}

	
	return %callCount;
}

function EventManager_ProcessMainStack(%currStack)
{
	if($EventManager_MainStack[%currStack, VALID] == false)
		return;

	$EventManager_CurrMainStack = %currStack;

	%callCount = EventManager_ProcessEvent(%currStack);

	//if the main stack has no sub-stacks left delete it
	if($EventManager_MainStack[%currStack, COUNT] == 0)
	{
		deleteVariables("$EventManager_MainStack" @ %currStack @ "_*"); //delete all the related stack vars
		talk("DELETED MAIN-STACK");

		if(%currStack == $EventManager_MainStackCount - 1)
			$EventManager_MainStackCount--;
	}

	$EventManager_CurrMainStack = -1;

	return %callCount;
}



function EventManager_Process()
{
	%callQuota = 10;
	for(%i = 0; %i < $EventManager_MainStackCount; %i++)
	{
		if(!$EventManager_MainStack[%i, VALID])
			continue;
		
		while(%callCount < %callQuota && $EventManager_MainStack[%i, VALID])
			%callCount += EventManager_ProcessMainStack(%i);

		if(%callCount >= %callQuota)
			break;
	}

	bottomprintall(%callCount, 1, 1);
	if(%callCount > %callQuota)
		talk(%callCount);
	$EventManager_Schedule = schedule(1 + %callCount, 0, EventManager_Process);
}

function SimObject::parseEventNumParameters(%target, %obj, %i, %numParameters, %delay, %client)
{
	%outputEvent = %obj.eventOutput[%i];
	%par1 = %obj.eventOutputParameter[%i, 1];
	%par2 = %obj.eventOutputParameter[%i, 2];
	%par3 = %obj.eventOutputParameter[%i, 3];
	%par4 = %obj.eventOutputParameter[%i, 4];

	if (%obj.eventOutputAppendClient[%i])
	{
		switch(%numParameters)
		{
			case 0:
				%scheduleID = %target.schedule(%delay, %outputEvent, %client);
			case 1:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %client);
			case 2:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2, %client);
			case 3:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2, %par3, %client);
			case 4:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2, %par3, %par4, %client);
			default:
				error("ERROR: SimObject::ProcessInputEvent() - bad number of parameters on event \'" @ %outputEvent @ "\' (" @ %numParameters @ ")");
		}
	} else {
		switch(%numParameters)
		{
			case 0:
				%scheduleID = %target.schedule(%delay, %outputEvent);
			case 1:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1);
			case 2:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2);
			case 3:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2, %par3);
			case 4:
				%scheduleID = %target.schedule(%delay, %outputEvent, %par1, %par2, %par3, %par4);
			default:
				error("ERROR: SimObject::ProcessInputEvent() - bad number of parameters on event \'" @ %outputEvent @ "\' (" @ %numParameters @ ")");
		}
	}


	if(%EventName !$= "onToolBreak") //special handling for an unused event
		%obj.addScheduledEvent(%scheduleID);

	return %scheduleID;
}

function SimObject::EM_parseEventNumParameters(%target, %obj, %i, %numParameters, %client)
{
	%outputEvent = %obj.eventOutput[%i];
	if(!isFunction(%target.getClassName(), %outputEvent)) //that is not a function
		return;
	
	%par1 = %obj.eventOutputParameter[%i, 1];
	%par2 = %obj.eventOutputParameter[%i, 2];
	%par3 = %obj.eventOutputParameter[%i, 3];
	%par4 = %obj.eventOutputParameter[%i, 4];

	if(%obj.eventOutputAppendClient[%i])
	{
		switch(%numParameters)
		{
			case 0:
				eval(%target @ "." @ %outputEvent @ "(%client);");
			case 1:
				eval(%target @ "." @ %outputEvent @ "(%par1, %client);");
			case 2:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2, %client);");
			case 3:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2, %par3, %client);");
			case 4:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2, %par3, %par4, %client);");
			default:
				error("ERROR: SimObject::ProcessInputEvent() - bad number of parameters on event \'" @ %outputEvent @ "\' (" @ %numParameters @ ")");
		}
	} else {
		switch(%numParameters)
		{
			case 0:
				eval(%target @ "." @ %outputEvent @ "()");
			case 1:
				eval(%target @ "." @ %outputEvent @ "(%par1);");
			case 2:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2);");
			case 3:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2, %par3);");
			case 4:
				eval(%target @ "." @ %outputEvent @ "(%par1, %par2, %par3, %par4);");
			default:
				error("ERROR: SimObject::ProcessInputEvent() - bad number of parameters on event \'" @ %outputEvent @ "\' (" @ %numParameters @ ")");
		}
	}

	return %scheduleID;
}
