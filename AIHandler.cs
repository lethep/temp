using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[ImplementsMiTreeLeafDelegate]
public class AIHandler : MiCharacterScope
{
	public enum AlertState
	{
		Idle,
		Suspicious,
		Alert,
		Distracted
	}

	[Serializable]
	[MiSaveStruct]
	public struct Data
	{
		public AlertState m_eAlertState;

		public int m_iDetectionActive;

		public int m_iDetectionCurrent;

		public Transform m_transTarget;

		public Vector3 m_v3TargetPos;

		public Transform m_transObjDetected;

		public ClickableObject m_clickableDetected;

		public CarryableObject m_carryableDetected;

		public MiCharacter m_charDetected;

		public NoiseEmitter m_noiseDetected;

		public bool m_bDangerDetected;

		public NoiseEmitter.SkillOnNoiseDetect m_skillOnDetectNoise;

		public MiCharacter m_charDistraction;

		public MiCharacterTanuki m_tanuki;

		public MiUsable m_usable;

		public MiUsableLadder m_ladderTargetIsOn;

		public AlarmSquad m_alarmSquad;

		public AlarmTargetInfo m_alarmTargetInfo;

		public float m_fLocalAlarmUntil;

		public float m_fLocalAlarmFreshUntil;

		public void reset(MiCharacterNPC _npc)
		{
			m_iDetectionActive = int.MaxValue;
			m_iDetectionCurrent = 0;
			m_transTarget = null;
			m_v3TargetPos = Vector3.zero;
			m_transObjDetected = null;
			m_noiseDetected = null;
			m_bDangerDetected = true;
			m_skillOnDetectNoise = null;
			m_clickableDetected = null;
			m_charDistraction = null;
			m_charDetected = null;
			m_tanuki = null;
			if ((bool)m_usable)
			{
				if (m_usable.bIsPreOccupiedBy(_npc))
				{
					m_usable.preRelease();
				}
				m_usable = null;
			}
			m_ladderTargetIsOn = null;
			m_alarmTargetInfo = null;
			m_fLocalAlarmUntil = 0f;
			m_fLocalAlarmFreshUntil = 0f;
		}
	}

	private enum HealthQuery
	{
		HealthState,
		BadFortune,
		Both
	}

	public enum AIBlockReason
	{
		GameDesignDecision,
		Routine,
		Skill,
		Cutscene,
		OffMesh
	}

	public delegate void delAlertStateChange(MiCharacterNPC _npc);

	private AISettingsGlobal m_aiSettings;

	private DetectionIndexBuffer m_detectionIndexBuffer = new DetectionIndexBuffer();

	public bool m_bIsStatic;

	private Data m_data;

	private float m_fDetectionDelayFinished = -1f;

	private float m_fDetectionDelayCooldownFinished = -1f;

	private float m_fDelayFinished;

	[FormerlySerializedAs("m_fLookAtDurationStaticEnemy")]
	public float m_fLookAtDangerDurationStatic = 4f;

	public float m_fLookAtDistractionDurationStatic = 2f;

	public DetectionData m_dataCoverLadder;

	private bool m_bDistractable;

	private Transform m_transThrowStart;

	private Transform m_transThrowEnd;

	[MiFlags(typeof(AlarmSystem.AlarmType))]
	public int m_iCanSignalAlarmTypes = 22;

	private MiCoroutine m_coroAlarmWhenInRange;

	private MiCoroutine m_coroSignalAlarmLocal;

	private MiCoroutine m_coroSignalAlarmArea;

	public float m_fStaticAlarmCooldownDuration = 5f;

	private float m_fTimeLocalAlarmCooldownReady;

	public float m_fLocalAlarmFreshDuration = 1f;

	private bool m_bWasAlarmedLastFrame;

	[FormerlySerializedAs("m_noiseSettings")]
	public NoiseEmitter.NoiseEmitterSettings m_noiseSettingsGeneric;

	[MiTooltip("Dont change NoiseType. Reaction duration is fLookAtDangerDurationStatic", MiTooltipAttribute.TooltipType.Tooltip)]
	public NoiseEmitter.NoiseEmitterSettings m_noiseSettingsSeeCorpse;

	private NoiseEmitter.SkillOnNoiseDetect m_skillNoise;

	public float m_fNoiseCooldown = 1f;

	private float m_fLastNoiseTime;

	private Dictionary<AlertState, delAlertStateChange> m_dictAlertStateEventsChangeTo = new Dictionary<AlertState, delAlertStateChange>();

	private Dictionary<AlertState, delAlertStateChange> m_dictAlertStateEventsChangeFrom = new Dictionary<AlertState, delAlertStateChange>();

	[FormerlySerializedAs("m_arSkillsIgnore")]
	public List<Skill.SkillType> m_liSkillsIgnore;

	public List<Skill.SkillType> m_liSkillsAllowedWhenStunned;

	private SkillLookAt.delegateAfterLookAt m_delAfterLookAt;

	private SkillLookAt.delegateAfterLookAtGeneric m_delAfterLookAtGeneric;

	private object[] m_arObjGenericDataAfterLookAt;

	private float? m_fLookAtDuration;

	private SkillSpreader m_skillSpreaderFormation;

	private SkillSpreader m_skillSpreaderRAction;

	private Skill.SkillType m_skillTypeToSpread;

	private MiCharacterTarget m_targetToSpread;

	private bool m_bSetProcessedOnSpread;

	private SkillSpreader.SpreadType m_spreadType;

	private Skill.SkillType m_skillTypeSetThisFrame;

	private Vector3 m_v3TargetPositionSetThisFrame;

	private MiCharacterMovementType.MovementType m_movementTypeAfterStun;

	private VariableLockSystemBool m_varLockAIBlocked = new VariableLockSystemBool(VariableLockSystemBool.RemoveLockCheckModeBool.False);

	private bool m_bAIIsBlocked;

	private bool m_bRoutineBlocked;

	public AlertState eAlertState => m_data.m_eAlertState;

	public Data data => m_data;

	public AlarmSquad alarmSquad => m_data.m_alarmSquad;

	public Transform transTarget => m_data.m_transTarget;

	public SkillSpreader skillSpreaderFormation
	{
		set
		{
			m_skillSpreaderFormation = value;
		}
	}

	public SkillSpreader skillSpreaderRAction
	{
		set
		{
			m_skillSpreaderRAction = value;
		}
	}

	public bool bRoutineBlocked
	{
		get
		{
			return m_bRoutineBlocked;
		}
		set
		{
			m_bRoutineBlocked = value;
		}
	}

	protected override void MiAwake()
	{
		m_data.reset(m_charNPC);
		m_data.m_eAlertState = AlertState.Idle;
	}

	protected override void MiStart()
	{
		m_aiSettings = MiSingletonSaveMortal<AISettingsGlobal>.instance;
	}

	public void reset()
	{
		if ((bool)alarmSquad)
		{
			alarmSquad.removeNPC(m_charNPC);
		}
		m_data.reset(m_charNPC);
		if (m_coroAlarmWhenInRange != null)
		{
			m_coroAlarmWhenInRange.stop(ref m_coroAlarmWhenInRange);
		}
		if (m_coroSignalAlarmLocal != null)
		{
			m_coroSignalAlarmLocal.stop(ref m_coroSignalAlarmLocal);
		}
		if (m_coroSignalAlarmArea != null)
		{
			m_coroSignalAlarmArea.stop(ref m_coroSignalAlarmArea);
		}
		setAlertState(AlertState.Idle);
	}

	public void setData(Data _data)
	{
		if (_data.m_eAlertState != m_data.m_eAlertState)
		{
			setAlertState(_data.m_eAlertState);
		}
		if (_data.m_alarmSquad != null && MiCharacter.bIsEnemy(m_charNPC.m_eCharacter))
		{
			_data.m_alarmSquad.addNPC(m_charNPC);
		}
		else
		{
			_data.m_alarmSquad = null;
		}
		m_data = _data;
	}

	public void setDataAlarmSquad(AlarmSquad _alarmSquad)
	{
		m_data.m_alarmSquad = _alarmSquad;
		if ((bool)_alarmSquad)
		{
			addDelegateOnAlertStateChangeTo(removeFromAlarmSquad, AlertState.Idle);
		}
	}

	private void removeFromAlarmSquad(MiCharacterNPC _npc)
	{
		if ((bool)_npc.m_ai.data.m_alarmSquad)
		{
			_npc.m_ai.alarmSquad.removeNPC(m_charNPC);
		}
	}

	public void setDataTarget(Transform _trans)
	{
		m_data.m_transTarget = _trans;
	}

	public MiTreeLeaf.Result _currentDetectionIncr(bool _bInit)
	{
		m_data.m_iDetectionCurrent++;
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setActiveDetection(bool _bInit)
	{
		m_data.m_iDetectionActive = m_data.m_iDetectionCurrent;
		m_detectionIndexBuffer.addEntry(m_data.m_iDetectionActive);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bHasPriority(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_data.m_iDetectionCurrent <= m_data.m_iDetectionActive);
	}

	public MiTreeLeaf.Result _resetActiveDetection(bool _bInit)
	{
		m_data.reset(m_charNPC);
		if ((bool)m_charNPC.m_detection)
		{
			m_charNPC.m_detection.restoreDefaultIgnoreTypes();
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _resetCurrentDetection(bool _bInit)
	{
		m_data.m_iDetectionCurrent = 0;
		m_data.m_bDangerDetected = true;
		m_bDistractable = false;
		if ((bool)m_charNPC.m_detection)
		{
			m_charNPC.m_detection.resetRangeChanged();
		}
		m_skillTypeSetThisFrame = Skill.SkillType.None;
		m_v3TargetPositionSetThisFrame = Vector3.zero;
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _noDangerDetected(bool _bInit)
	{
		m_data.m_bDangerDetected = false;
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _delayDetection(bool _bInit)
	{
		float time = MiTime.time;
		if (m_fDetectionDelayFinished < time && m_fDetectionDelayCooldownFinished > time)
		{
			return MiTreeLeaf.Result.Success;
		}
		if (_bInit && !m_charNPC.m_detection.focusedObject.m_bWasVisibleOutsideOfDetectionRange)
		{
			m_fDetectionDelayFinished = time + DifficultySettings.m_difficultySettingsCurrent.m_fDetectionDelayYellowVC;
			m_fDetectionDelayCooldownFinished = time + m_aiSettings.m_fDetectionDelayCooldown;
		}
		if (time >= m_fDetectionDelayFinished)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Running;
	}

	public MiTreeLeaf.Result _triggerCmdOnDetect(bool _bInit)
	{
		if ((bool)m_charNPC.m_cmdOnDetect)
		{
			m_charNPC.m_cmdOnDetect.trigger();
		}
		return MiTreeLeaf.Result.Success;
	}

	public void addObjectToMemory(Transform _trans)
	{
		m_charNPC.m_aiMemory.add(_trans);
	}

	public MiTreeLeaf.Result _addObjectToMemory(bool _bInit)
	{
		addObjectToMemory(m_data.m_transObjDetected);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _addTargetToMemory(bool _bInit)
	{
		addObjectToMemory(m_data.m_transTarget);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _getObject(bool _bInit)
	{
		if (_bInit && m_charNPC.m_detection.focusedObject != null)
		{
			m_data.m_transObjDetected = m_charNPC.m_detection.focusedObject.objRef.trans;
			m_data.m_clickableDetected = m_charNPC.m_detection.focusedObject.objRef;
		}
		return MiTreeLeaf.boolToResult(m_data.m_clickableDetected != null);
	}

	public MiTreeLeaf.Result _ignoreDetectedCharacter(bool _bInit)
	{
		if ((bool)m_data.m_charDetected)
		{
			m_charNPC.m_detection.ignoreType(m_data.m_charDetected.GetType());
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bDetectionCountAboveThreshold(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_detectionIndexBuffer.entriesWithIndex(m_data.m_iDetectionCurrent) >= MiSingletonSaveMortal<AISettingsGlobal>.instance.m_iDetectionRepetitionThreshold);
	}

	public MiTreeLeaf.Result _delayShort(bool _bInit)
	{
		if (_bInit)
		{
			m_fDelayFinished = MiTime.time + m_aiSettings.m_fDelayShort;
		}
		if (m_fDelayFinished < MiTime.time)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Running;
	}

	public MiTreeLeaf.Result _delayShortInterval(bool _bInit)
	{
		if (_bInit)
		{
			m_fDelayFinished = MiTime.time + m_aiSettings.fDelayShortInterval;
		}
		if (m_fDelayFinished < MiTime.time)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Running;
	}

	public MiTreeLeaf.Result _delayLong(bool _bInit)
	{
		if (_bInit)
		{
			m_fDelayFinished = MiTime.time + m_aiSettings.m_fDelayLong;
		}
		if (m_fDelayFinished < MiTime.time)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Running;
	}

	public float fStaticLookAtDuration(bool _bDangerDetected)
	{
		if (_bDangerDetected)
		{
			return m_fLookAtDangerDurationStatic;
		}
		return m_fLookAtDistractionDurationStatic;
	}

	public Vector3 getLookAtPosition(Vector3 _v3Pos)
	{
		if (MiMath.bLossyCompare(_v3Pos, m_character.trans.position, 0.1f))
		{
			_v3Pos = m_character.trans.position + m_character.trans.forward * 1.5f;
		}
		return _v3Pos;
	}

	public MiTreeLeaf.Result _setSkillLookAtGeisha(bool _bInit)
	{
		setSkill(Skill.SkillType.LookAtGeisha, new MiCharacterTargetLookAt(m_data.m_charDistraction.trans.position, 0f));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillLookAtNoise(bool _bInit)
	{
		setSkillLookAtNoise(m_bIsStatic || MiCharacter.bIsSamurai(m_character.m_eCharacter));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillLookAtNoiseStatic(bool _bInit)
	{
		setSkillLookAtNoise(bStatic: true);
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillLookAtNoise(bool bStatic)
	{
		if (m_data.m_noiseDetected == null)
		{
			m_data.m_noiseDetected = m_charNPC.m_noiseDetection.detectedNoise;
		}
		float num = 0f;
		MiCharacterTarget target = new MiCharacterTargetLookAt(_fLookAtDuration: (!bStatic && !(m_data.m_noiseDetected == null)) ? m_data.m_noiseDetected.m_fReactionDuration : fStaticLookAtDuration(m_data.m_bDangerDetected), _v3LookAt: (!m_data.m_noiseDetected) ? m_data.m_v3TargetPos : m_data.m_noiseDetected.trans.position, _fAngularSpeedFactor: 400f);
		setSkill(Skill.SkillType.LookAt, target, SkillSpreader.SpreadType.Wait);
	}

	public MiTreeLeaf.Result _setSkillInvestigateAfterLookAt(bool _bInit)
	{
		int iDetectionCurrent = m_data.m_iDetectionCurrent;
		bool bDangerDetected = m_data.m_bDangerDetected;
		MiCharacterTarget miCharacterTarget = new MiCharacterTargetInvestigate(getLookAtPosition(m_data.m_v3TargetPos), m_data.m_transTarget, MiCharacterMovementType.MovementType.Aiming, iDetectionCurrent, null, null, _bInvestigateFar: true, m_data.m_bDangerDetected, _bInvestigateAccident: false, bDangerDetected, _bInvestigateMissing: false, null, 200f, 0f, 0f);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillInvestigate;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillInvestigateThrownCorpseAfterLookAt(bool _bInit)
	{
		int iDetectionCurrent = m_data.m_iDetectionCurrent;
		MiCharacterTarget miCharacterTarget = new MiCharacterTargetInvestigate(getLookAtPosition(m_data.m_v3TargetPos), m_data.m_transTarget, MiCharacterMovementType.MovementType.Aiming, iDetectionCurrent, null, null, _bInvestigateFar: true, _bInvestigateDoor: false, _bInvestigateAccident: false, m_data.m_bDangerDetected, _bInvestigateMissing: false, null, 200f, 0f, 0f);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillInvestigateCorpse;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillInvestigateCorpse(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.Investigate, _arObjData[0] as MiCharacterTarget);
		_npc.m_ai._signalAlarmLocal(_bInit: true);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setSkillInvestigateWhistleNPCAfterLookAt(bool _bInit)
	{
		int iDetectionCurrent = m_data.m_iDetectionCurrent;
		bool bDangerDetected = m_data.m_bDangerDetected;
		MiCharacterTargetInvestigate miCharacterTargetInvestigate = new MiCharacterTargetInvestigate(getLookAtPosition(m_data.m_v3TargetPos), m_data.m_transTarget, MiCharacterMovementType.MovementType.Aiming, iDetectionCurrent, null, null, _bInvestigateFar: false, _bInvestigateDoor: false, _bInvestigateAccident: false, bDangerDetected, _bInvestigateMissing: false, null, 200f, 0f, 0f);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillInvestigate;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTargetInvestigate };
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillInvestigateWhistleAfterLookAt(bool _bInit)
	{
		int iDetectionCurrent = m_data.m_iDetectionCurrent;
		bool bDangerDetected = m_data.m_bDangerDetected;
		MiCharacterTargetInvestigate miCharacterTargetInvestigate = new MiCharacterTargetInvestigate(getLookAtPosition(m_data.m_v3TargetPos), m_data.m_transTarget, MiCharacterMovementType.MovementType.Aiming, iDetectionCurrent, null, null, _bInvestigateFar: false, _bInvestigateDoor: false, _bInvestigateAccident: false, bDangerDetected, _bInvestigateMissing: false, null, 200f, 0f, 0f);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillInvestigate;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTargetInvestigate };
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillInvestigateWhistleRunAfterLookAt(bool _bInit)
	{
		bool bDangerDetected = m_data.m_bDangerDetected;
		MiCharacterTargetInvestigate miCharacterTargetInvestigate = new MiCharacterTargetInvestigate(getLookAtPosition(m_data.m_v3TargetPos), m_data.m_transTarget, MiCharacterMovementType.MovementType.Run, m_data.m_iDetectionCurrent, null, null, _bInvestigateFar: false, _bInvestigateDoor: false, _bInvestigateAccident: false, bDangerDetected, _bInvestigateMissing: false, null, 200f, 0f, 0f);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillInvestigate;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTargetInvestigate };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillInvestigate(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.Investigate, _arObjData[0] as MiCharacterTarget);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setSkillLookAtAndSuspiciousAfterLookAt(bool _bInit)
	{
		MiCharacterTarget miCharacterTarget = new MiCharacterTargetLookAt(getLookAtPosition(m_data.m_v3TargetPos), fStaticLookAtDuration(m_data.m_bDangerDetected));
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillLookAtAndSuspicious;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillLookAtAndSuspicious(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setAlertState(AlertState.Suspicious);
		_npc.m_ai.setSkill(Skill.SkillType.LookAt, _arObjData[0] as MiCharacterTarget, SkillSpreader.SpreadType.Wait, _bSetProcessed: true);
		_npc.m_detection.addDataPreset(DetectionDataPresets.PresetName.Standard);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setSkillLookAtDistractionObject(bool _bInit)
	{
		MiCharacterTarget target = new MiCharacterTargetLookAt(m_data.m_clickableDetected.trans.position, fStaticLookAtDuration(m_data.m_bDangerDetected));
		setSkill(Skill.SkillType.LookAt, target, SkillSpreader.SpreadType.Wait);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillLookAtTarget(bool _bInit)
	{
		MiCharacterTarget target = new MiCharacterTargetLookAt(getLookAtPosition(m_data.m_v3TargetPos), fStaticLookAtDuration(m_data.m_bDangerDetected), 400f);
		setSkill(Skill.SkillType.LookAt, target, SkillSpreader.SpreadType.Wait);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillLookAtTargetDontLookAroundAfter(bool _bInit)
	{
		MiCharacterTarget target = new MiCharacterTargetLookAt(getLookAtPosition(m_data.m_v3TargetPos), fStaticLookAtDuration(m_data.m_bDangerDetected), 400f, _bLookAroundAfter: false);
		setSkill(Skill.SkillType.LookAt, target, SkillSpreader.SpreadType.Wait);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillLookAtFootprint(bool _bInit)
	{
		ClickableObject clickableDetected = m_data.m_clickableDetected;
		MiCharacterTarget target = new MiCharacterTargetLookAt(m_data.m_clickableDetected.trans.position, fStaticLookAtDuration(_bDangerDetected: false), 200f, _bLookAroundAfter: true, _bDecreaseDetectionRange: false, _bGetSuspicious: false, null, null, null, null, DetectionOrientation.TurnDirection.None, SkillLookAt.TurnDirection.Auto, SkillLookAt.TurnAnimationStyle.Standing, null, clickableDetected);
		setSkill(Skill.SkillType.LookAtFootprints, target, SkillSpreader.SpreadType.Wait);
		writeFootprintsInRangeToMemory(m_data.m_clickableDetected as Footprint);
		return MiTreeLeaf.Result.Success;
	}

	private void writeFootprintsInRangeToMemory(Footprint _fpStart)
	{
		Footprint footprint = _fpStart;
		do
		{
			m_charNPC.m_aiMemory.add(footprint.trans);
			footprint = footprint.footprintNext;
		}
		while (footprint != null);
	}

	public MiTreeLeaf.Result _setSkillLookAtAnnoyed(bool _bInit)
	{
		MiCharacterTarget target = new MiCharacterTargetLookAt(m_data.m_v3TargetPos, 0f, 400f);
		setSkill(Skill.SkillType.LookAtAnnoyed, target);
		return MiTreeLeaf.Result.Success;
	}

	public void setSkillLookAtExtern(Transform _transTarget, bool _bPauseRoutines = true, float? _fDuration = null, bool _bDecreaseDetectionRange = true, SkillSpreader.SpreadType _spreadType = SkillSpreader.SpreadType.Wait, float _fAngularSpeedFactor = 400f)
	{
		m_data.m_transTarget = _transTarget;
		setSkillLookAtExtern(getLookAtPosition(_transTarget.position), _bPauseRoutines, _fDuration, _bDecreaseDetectionRange, _spreadType, _fAngularSpeedFactor);
	}

	public void setSkillLookAtExtern(Vector3 _v3Target, bool _bPauseRoutines = true, float? _fDuration = null, bool _bDecreseDetectionRange = true, SkillSpreader.SpreadType _spreadType = SkillSpreader.SpreadType.Wait, float _fAngularSpeedFactor = 400f)
	{
		if (_bPauseRoutines)
		{
			m_charNPC.routineSystem.pauseRoutines();
		}
		m_charNPC.m_detectionOrientation.endFocus();
		MiCharacterTargetLookAt target = new MiCharacterTargetLookAt(_v3Target, new float?((!_fDuration.HasValue) ? fStaticLookAtDuration(m_data.m_bDangerDetected) : _fDuration.Value).Value, _fAngularSpeedFactor);
		setSkill(Skill.SkillType.LookAt, target, _spreadType);
		_spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setTargetObject(bool _bInit)
	{
		if (m_data.m_transObjDetected != null)
		{
			setTarget(m_data.m_transObjDetected);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetNoise(bool _bInit)
	{
		m_data.m_noiseDetected = m_charNPC.m_noiseDetection.detectedNoise;
		if (m_data.m_noiseDetected != null)
		{
			m_data.m_skillOnDetectNoise = m_data.m_noiseDetected.skillOnNoiseDetect;
			if (m_data.m_skillOnDetectNoise != null && m_data.m_skillOnDetectNoise.m_transTarget != null)
			{
				setTarget(m_data.m_skillOnDetectNoise.m_transTarget);
			}
			else
			{
				setTarget(m_data.m_noiseDetected.trans);
			}
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetAlarmLocal(bool _bInit)
	{
		if (m_data.m_alarmTargetInfo != null)
		{
			setTarget(m_data.m_alarmTargetInfo.transTarget, m_data.m_alarmTargetInfo.v3TargetPos);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetAlarmLocalSource(bool _bInit)
	{
		if (m_data.m_alarmTargetInfo != null)
		{
			setTarget(m_data.m_alarmTargetInfo.transSource, m_data.m_alarmTargetInfo.v3SourcePos);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetCollision(bool _bInit)
	{
		Transform transPlayerCollision = m_character.m_charAvoidanceDetection.getTransPlayerCollision();
		if ((bool)transPlayerCollision)
		{
			setTarget(transPlayerCollision);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetProximityObject(bool _bInit)
	{
		MiCharacterPlayer playerDetected = m_charNPC.m_detection.m_proximityDetection.playerDetected;
		if ((bool)playerDetected)
		{
			setTarget(playerDetected.trans);
			m_data.m_charDetected = playerDetected;
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setTargetThrownObject(bool _bInit)
	{
		if ((bool)m_transThrowStart)
		{
			setTarget(m_transThrowStart);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public void setTarget(Transform _transTarget, Vector3? _v3TargetPos = null)
	{
		m_data.m_transTarget = _transTarget;
		if (_v3TargetPos.HasValue)
		{
			m_data.m_v3TargetPos = _v3TargetPos.Value;
		}
		else
		{
			m_data.m_v3TargetPos = _transTarget.position;
		}
	}

	public MiTreeLeaf.Result _bTargetAtCharPos(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiMath.fDistanceXZ(m_data.m_v3TargetPos, m_character.trans.position) <= m_aiSettings.m_fOffsetDetectAtCharPos);
	}

	public MiTreeLeaf.Result _setSkillInvestigate(bool _bInit)
	{
		if (m_bIsStatic)
		{
			return _setSkillLookAtTarget(_bInit: true);
		}
		setSkill(Skill.SkillType.Investigate, new MiCharacterTargetInvestigate(_iDetectionIndex: m_data.m_iDetectionCurrent, _v3Target: m_data.m_v3TargetPos, _transTarget: m_data.m_transTarget, _moveType: MiCharacterMovementType.MovementType.Aiming, _enumerableInvPoints: null, _iInvestigatePoints: null, _bInvestigateFar: true, _bInvestigateDoor: false, _bInvestigateAccident: false, _bInvestigateDanger: m_data.m_bDangerDetected, _bInvestigateMissing: false, _transReinforcements: null, _fAngularSpeedFactor: 400f, _fLookAtDuration: 0f, _fRangeTrackedObject: 0f));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillInvestigateForced(bool _bInit)
	{
		setSkill(Skill.SkillType.Investigate, new MiCharacterTargetInvestigate(_iDetectionIndex: m_data.m_iDetectionCurrent, _v3Target: m_data.m_v3TargetPos, _transTarget: m_data.m_transTarget, _moveType: MiCharacterMovementType.MovementType.Aiming, _enumerableInvPoints: null, _iInvestigatePoints: null, _bInvestigateFar: true, _bInvestigateDoor: false, _bInvestigateAccident: false, _bInvestigateDanger: m_data.m_bDangerDetected, _bInvestigateMissing: false, _transReinforcements: null, _fAngularSpeedFactor: 400f, _fLookAtDuration: 0f, _fRangeTrackedObject: 0f));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillInvestigateBadFortune(bool _bInit)
	{
		if (m_bIsStatic)
		{
			return _setSkillLookAtTarget(_bInit: true);
		}
		setSkill(Skill.SkillType.InvestigateBadFortune, new MiCharacterTarget(m_data.m_charDetected, m_data.m_transTarget));
		return MiTreeLeaf.Result.Success;
	}

	public bool setSkillInvestigateExtern(MiCharacterTargetInvestigate _target, bool _bForced = false, AlertState? _eAlertState = null)
	{
		if (m_bIsStatic && !_bForced)
		{
			return false;
		}
		m_charNPC.routineSystem.pauseRoutines();
		if (_eAlertState.HasValue)
		{
			setAlertState(_eAlertState.Value);
		}
		bool flag = setSkill(Skill.SkillType.Investigate, _target);
		if (flag)
		{
			_spreadSkill(_bInit: true);
		}
		return flag;
	}

	public MiTreeLeaf.Result _bTargetCharOnLadder(bool _bInit)
	{
		MiCharacter charDetected = m_data.m_charDetected;
		if (m_data.m_ladderTargetIsOn == null && (bool)charDetected && charDetected.skillCommandActive != null)
		{
			m_data.m_ladderTargetIsOn = charDetected.skillCommandActive.m_MiCharacterTarget.m_MiBaseComponent as MiUsableLadder;
		}
		return MiTreeLeaf.boolToResult(m_data.m_ladderTargetIsOn != null);
	}

	public MiTreeLeaf.Result _bTargetOnLadder(bool _bInit)
	{
		if (m_data.m_ladderTargetIsOn == null)
		{
			Collider[] array = Physics.OverlapSphere(m_character.getMiCharacterTarget().m_v3TargetPos, 0.75f, 40960);
			for (int i = 0; i < array.Length; i++)
			{
				ClickableObjectReference component = array[i].GetComponent<ClickableObjectReference>();
				if ((bool)component)
				{
					m_data.m_ladderTargetIsOn = component.m_clickableObject as MiUsableLadder;
					if ((bool)m_data.m_ladderTargetIsOn)
					{
						break;
					}
				}
			}
		}
		return MiTreeLeaf.boolToResult(m_data.m_ladderTargetIsOn != null);
	}

	public MiTreeLeaf.Result _bCantDetectWholeLadder(bool _bInit)
	{
		bool flag = !m_charNPC.m_detection.bInYRange(m_data.m_ladderTargetIsOn.m_transEntryA.position);
		bool flag2 = !m_charNPC.m_detection.bInYRange(m_data.m_ladderTargetIsOn.m_transEntryB.position);
		return MiTreeLeaf.boolToResult(flag || flag2);
	}

	public MiTreeLeaf.Result _setDetectionDataToCoverLadder(bool _bInit)
	{
		if (m_dataCoverLadder == null)
		{
			m_dataCoverLadder = SaveLoadSceneManager.MiAddComponent<DetectionData>(base.gameObject);
		}
		m_dataCoverLadder.copyValues(m_charNPC.m_detection.m_dataSwitcher.getActive());
		MiUsableLadder ladderTargetIsOn = m_data.m_ladderTargetIsOn;
		bool flag = m_charNPC.m_detection.bInYRange(ladderTargetIsOn.m_transEntryA.position);
		bool flag2 = m_charNPC.m_detection.bInYRange(ladderTargetIsOn.m_transEntryB.position);
		float num = m_charNPC.trans.position.y + m_charNPC.m_detection.m_fEyeHeight;
		if (!flag)
		{
			float y = ladderTargetIsOn.m_transEntryA.position.y;
			m_dataCoverLadder.setVisionCapBottom(num - y + 0.8f);
		}
		if (!flag2)
		{
			float y2 = ladderTargetIsOn.m_transEntryB.position.y;
			m_dataCoverLadder.setNoVisionHeight(y2 + 1.8f - num);
		}
		if (!m_charNPC.m_detection.m_dataSwitcher.add(m_dataCoverLadder, null, 0f))
		{
			m_charNPC.m_detection.m_dataSwitcher.setDataActive(m_dataCoverLadder);
		}
		addDelegateOnAlertStateChangeTo(removeDataCoverLadder, AlertState.Idle);
		return MiTreeLeaf.Result.Success;
	}

	private void removeDataCoverLadder(MiCharacterNPC _npc)
	{
		_npc.m_detection.m_dataSwitcher.remove(m_dataCoverLadder);
	}

	public MiTreeLeaf.Result _bIsUsableObject(bool _bInit)
	{
		if (_bInit)
		{
			m_data.m_usable = m_data.m_clickableDetected.GetComponent<MiUsable>();
		}
		return MiTreeLeaf.boolToResult(m_data.m_usable != null);
	}

	public MiTreeLeaf.Result _bIsUseable(bool _bInit)
	{
		if (m_data.m_usable.bIsFree() && (!m_data.m_usable.bIsPreOccupied() || m_data.m_usable.bIsPreOccupiedBy(m_charNPC)))
		{
			m_data.m_usable.preOccupy(m_charNPC);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _setSkillUseAfterLookAt(bool _bInit)
	{
		MiUsable usable = m_data.m_usable;
		MiCharacterTarget miCharacterTarget = new MiCharacterTarget(usable, usable.trans);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillUse;
		m_arObjGenericDataAfterLookAt = new object[2] { miCharacterTarget, usable };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillUse(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.Use, _arObjData[0] as MiCharacterTarget, SkillSpreader.SpreadType.Follow);
		(_arObjData[1] as MiUsable).preOccupy(_npc);
		_npc.m_evOnDeath.Add(preReleaseUsable);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setSkillUseLightSourceAfterLookAt(bool _bInit)
	{
		IMultipleEntriesUsable multipleEntriesUsable = m_data.m_usable as IMultipleEntriesUsable;
		Transform transform = m_data.m_usable.trans;
		if (multipleEntriesUsable != null)
		{
			Transform[] entryPoints = multipleEntriesUsable.getEntryPoints();
			transform = entryPoints[0];
			float num = float.MaxValue;
			for (int i = 0; i < entryPoints.Length; i++)
			{
				if (m_charNPC.bCanReachPosition(entryPoints[i].position, out var _fPathLength, null, _bPartial: false, _bCalcLength: true) && _fPathLength < num)
				{
					num = _fPathLength;
					transform = entryPoints[i];
				}
			}
		}
		MiCharacterTarget miCharacterTarget = new MiCharacterTarget(m_data.m_usable, transform);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillUseLightSource;
		m_arObjGenericDataAfterLookAt = new object[2] { miCharacterTarget, m_data.m_usable };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillUseLightSource(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.UseLightSource, _arObjData[0] as MiCharacterTarget, SkillSpreader.SpreadType.Follow);
		MiUsable miUsable = _arObjData[1] as MiUsable;
		miUsable.preOccupy(m_charNPC);
		_npc.m_evOnDeath.Add(preReleaseUsable);
		_npc.m_detection.addDataPreset(DetectionDataPresets.PresetName.Standard);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	private void preReleaseUsable()
	{
		if ((bool)m_data.m_usable)
		{
			m_data.m_usable.preRelease();
		}
	}

	public MiTreeLeaf.Result _bIsTanuki(bool _bInit)
	{
		if (_bInit)
		{
			m_data.m_tanuki = m_data.m_clickableDetected as MiCharacterTanuki;
		}
		return MiTreeLeaf.boolToResult(m_data.m_tanuki != null);
	}

	public MiTreeLeaf.Result _setSkillWatchTanuki(bool _bInit)
	{
		setSkill(Skill.SkillType.WatchTanuki, new MiCharacterTargetLookAt(_clickableTrackedObject: m_data.m_tanuki, _v3LookAt: m_data.m_tanuki.trans.position, _fLookAtDuration: 0f));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillWatchTanukiAfterLookAt(bool _bInit)
	{
		Transform transTargetOverride = m_data.m_transTarget;
		MiCharacterTarget miCharacterTarget = new MiCharacterTargetLookAt(m_data.m_noiseDetected.trans.position, 0f, 200f, _bLookAroundAfter: true, _bDecreaseDetectionRange: false, _bGetSuspicious: false, null, null, null, null, DetectionOrientation.TurnDirection.None, SkillLookAt.TurnDirection.Auto, SkillLookAt.TurnAnimationStyle.Standing, null, null, null, transTargetOverride);
		m_fLookAtDuration = m_aiSettings.m_fDelayShort;
		m_delAfterLookAtGeneric = setSkillWatchTanuki;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillWatchTanuki(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.WatchTanuki, _arObjData[0] as MiCharacterTarget);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _setDistractable(bool _bInit)
	{
		m_bDistractable = true;
		return MiTreeLeaf.Result.Success;
	}

	public bool bDistractable()
	{
		return m_bDistractable;
	}

	public MiTreeLeaf.Result _bIsDistractedByGeisha(bool _bInit)
	{
		m_data.m_charDistraction = m_charNPC.isDistractedBy();
		if (m_data.m_charDistraction != null)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _bIsBarrel(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_data.m_clickableDetected is IDistractionObject);
	}

	public MiTreeLeaf.Result _setSkillGoToBarrelAfterLookAt(bool _bInit)
	{
		m_fLookAtDuration = fStaticLookAtDuration(m_data.m_bDangerDetected);
		m_delAfterLookAtGeneric = setSkillGoToBarrel;
		MiCharacterSoundNPC miCharacterSoundNPC = m_character.m_charSound as MiCharacterSoundNPC;
		MiCharacterTargetGoTo miCharacterTargetGoTo = new MiCharacterTargetGoTo(m_data.m_v3TargetPos, miCharacterSoundNPC.m_characterSoundDataNPC.m_sfxReactToBarrel, MiCharacterAnimation.AnimationState.ReactToBarrel);
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTargetGoTo };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillGoToBarrel(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.GoTo, _arObjData[0] as MiCharacterTarget, SkillSpreader.SpreadType.Wait);
		_npc.m_ai._spreadSkill(_bInit: true);
	}

	public MiTreeLeaf.Result _bDetectedCharIsAlive(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit);
	}

	public MiTreeLeaf.Result _bDetectedCharIsKnockedOut(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.Knockout);
	}

	public MiTreeLeaf.Result _bDetectedCharIsInProcessOfBeingKnockedOut(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.InProcessOfBeingKnockedOut);
	}

	public MiTreeLeaf.Result _bDetectedCharIsDying(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.Dying);
	}

	public MiTreeLeaf.Result _bDetectedCharIsDead(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.Dead);
	}

	public MiTreeLeaf.Result _bDetectedCharHadBadFortune(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.Dead, HealthQuery.BadFortune);
	}

	public MiTreeLeaf.Result _bDetectedCharDyingFromBadFortune(bool _bInit)
	{
		return bCharacterHealthStateQuery(_bInit, MiCharacterHealth.HealthState.Dying, HealthQuery.Both);
	}

	public MiTreeLeaf.Result _bDetectedCharIsFocused(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiSingletonSaveMortal<DetectionEvents>.instance.bObjectFocused(m_data.m_charDetected));
	}

	public MiTreeLeaf.Result _bDetectedCharWasDisguised(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_charNPC.m_detection.focusedObject.m_iDisguiseLevelOnDetect > MiConstants.iDisguiseLevelToInt(MiConstants.DisguiseLevel.Enemy));
	}

	private MiTreeLeaf.Result bCharacterHealthStateQuery(bool _bInit, MiCharacterHealth.HealthState _healthState = MiCharacterHealth.HealthState.Alive, HealthQuery _query = HealthQuery.HealthState)
	{
		if ((_bInit || m_data.m_charDetected == null) && m_charNPC.m_detection.focusedObject != null)
		{
			m_data.m_charDetected = m_charNPC.m_detection.focusedObject.objRef as MiCharacter;
			m_data.m_carryableDetected = m_data.m_charDetected;
		}
		if (m_data.m_charDetected == null)
		{
			return MiTreeLeaf.Result.Failure;
		}
		m_data.m_transObjDetected = m_data.m_charDetected.trans;
		return _query switch
		{
			HealthQuery.BadFortune => MiTreeLeaf.boolToResult(m_data.m_charDetected.m_charHealth.bBadFortune), 
			HealthQuery.HealthState => MiTreeLeaf.boolToResult(!m_data.m_charDetected.m_charHealth.bBadFortune && m_data.m_charDetected.m_charHealth.eHealthState == _healthState), 
			HealthQuery.Both => MiTreeLeaf.boolToResult(m_data.m_charDetected.m_charHealth.bBadFortune && m_data.m_charDetected.m_charHealth.eHealthState == _healthState), 
			_ => MiTreeLeaf.Result.Failure, 
		};
	}

	public MiTreeLeaf.Result _bObjectWasThrown(bool _bInit)
	{
		return MiTreeLeaf.boolToResult((bool)m_data.m_carryableDetected && m_data.m_carryableDetected.bWasThrownRecently);
	}

	public MiTreeLeaf.Result _createTemporaryTargetTransforms(bool _bInit)
	{
		m_transThrowStart = MiSingletonSaveLazyMortal<MiPoolDynamicHandler>.instance.instantiateRoot(MiPoolDynamicHandler.s_transEmptyPooling);
		m_transThrowEnd = MiSingletonSaveLazyMortal<MiPoolDynamicHandler>.instance.instantiateRoot(MiPoolDynamicHandler.s_transEmptyPooling);
		m_transThrowStart.position = m_data.m_carryableDetected.v3ThrownFrom;
		m_transThrowEnd.position = m_data.m_carryableDetected.v3ThrownTo;
		m_charNPC.routineSystem.addDelOnResume(m_charNPC, discardTemporaryTargetTransforms);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _addObjectToMemoryAtThrowTarget(bool _bInit)
	{
		MiSingletonSaveMortal<AIMemoryEvents>.instance.addToMemoryAtPosition(m_data.m_transObjDetected, m_transThrowEnd.position);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _ignoreThrownObject(bool _bInit)
	{
		CarryableObject carryableDetected = m_data.m_carryableDetected;
		m_charNPC.m_detection.ignoreObject(carryableDetected);
		m_charNPC.routineSystem.addDelOnResume(m_charNPC, clearIgnoreObjects);
		return MiTreeLeaf.Result.Success;
	}

	private bool clearIgnoreObjects(MiCharacterNPC _npc)
	{
		_npc.m_detection.clearIgnoreObjects();
		return true;
	}

	private bool discardTemporaryTargetTransforms(MiCharacterNPC _npc)
	{
		_npc.m_ai.m_transThrowStart.gameObject.SetActive(value: false);
		_npc.m_ai.m_transThrowEnd.gameObject.SetActive(value: false);
		return true;
	}

	public MiTreeLeaf.Result _bTargetIsFakeNoise(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bTargetIsNoiseType(NoiseDetection.NoiseType.FakeNoise));
	}

	private bool bTargetIsNoiseType(NoiseDetection.NoiseType _noiseType)
	{
		return (bool)m_data.m_noiseDetected && m_data.m_noiseDetected.m_eNoiseType == _noiseType;
	}

	public MiTreeLeaf.Result _signalAlarmLocal(bool _bInit)
	{
		if (MiFlags.FlagContains(m_iCanSignalAlarmTypes, 1))
		{
			signalAlarm(m_data.m_charDetected, m_data.m_transTarget, AlarmSystem.AlarmType.Local);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _signalAlarmLocalNoVO(bool _bInit)
	{
		if (MiFlags.FlagContains(m_iCanSignalAlarmTypes, 1))
		{
			signalAlarm(m_data.m_charDetected, m_data.m_transTarget, AlarmSystem.AlarmType.Local, null, _bPlayVO: false);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _signalAlarmLocalCooldown(bool _bInit)
	{
		if (!MiFlags.FlagContains(m_iCanSignalAlarmTypes, 1))
		{
			return MiTreeLeaf.Result.Success;
		}
		if (_bInit && m_fTimeLocalAlarmCooldownReady <= MiTime.time)
		{
			signalAlarm(m_data.m_charDetected, m_data.m_transTarget, AlarmSystem.AlarmType.Local);
			m_fTimeLocalAlarmCooldownReady = MiTime.time + m_fStaticAlarmCooldownDuration;
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _signalAlarmArea(bool _bInit)
	{
		signalAlarm(m_data.m_charDetected, m_data.m_transTarget, AlarmSystem.AlarmType.Area);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _signalAlarmAreaNoVO(bool _bInit)
	{
		signalAlarm(m_data.m_charDetected, m_data.m_transTarget, AlarmSystem.AlarmType.Area, null, _bPlayVO: false);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _signalAlarmWhenInRange(bool _bInit)
	{
		if (_bInit)
		{
			this.MiStartCoroutine(signalAlarmWhenInRange(m_data.m_charDetected, m_data.m_transTarget), ref m_coroAlarmWhenInRange);
		}
		return MiTreeLeaf.Result.Success;
	}

	private IEnumerator signalAlarmWhenInRange(MiCharacter _charTarget, Transform _transTarget)
	{
		Vector3 v3CharPos = _charTarget.trans.position;
		while (Vector3.Distance(v3CharPos, m_charNPC.trans.position) > m_aiSettings.m_fAlarmDistanceToCorpse)
		{
			yield return null;
		}
		if (MiMath.bLossyCompare(_charTarget.trans.position, v3CharPos, 0.1f))
		{
			if (MiFlags.FlagContains(m_iCanSignalAlarmTypes, 1) && bIsAlarmed())
			{
				signalAlarm(_charTarget, _transTarget, AlarmSystem.AlarmType.Local, null, _bPlayVO: false);
			}
			if (MiFlags.FlagContains(m_iCanSignalAlarmTypes, 2))
			{
				MiCharacterSoundNPC charSoundNPC = m_charNPC.m_charSound as MiCharacterSoundNPC;
				MiSfxInfo sfxDeadFriend = ((_charTarget.m_charVis.m_eGender != 0) ? charSoundNPC.m_characterSoundDataNPC.m_sfxDetectDeadFriendFemale : charSoundNPC.m_characterSoundDataNPC.m_sfxDetectDeadFriendMale);
				signalAlarm(_charTarget, _transTarget, AlarmSystem.AlarmType.Area, sfxDeadFriend);
			}
		}
	}

	public void signalAlarm(MiCharacter _charTarget, Transform _transTarget, AlarmSystem.AlarmType _eAlarmType, MiSfxInfo _sfxVOOverride = null, bool _bPlayVO = true)
	{
		if (_eAlarmType == AlarmSystem.AlarmType.Local && (m_coroSignalAlarmLocal == null || m_coroSignalAlarmLocal.bFinished) && MiFlags.FlagContains(m_iCanSignalAlarmTypes, 1))
		{
			this.MiStartCoroutine(signalAlarmDelayed(_charTarget, _transTarget, _eAlarmType, _sfxVOOverride, _bPlayVO), ref m_coroSignalAlarmLocal);
		}
		else if (_eAlarmType == AlarmSystem.AlarmType.Area && (m_coroSignalAlarmArea == null || m_coroSignalAlarmArea.bFinished) && MiFlags.FlagContains(m_iCanSignalAlarmTypes, 2))
		{
			this.MiStartCoroutine(signalAlarmDelayed(_charTarget, _transTarget, _eAlarmType, _sfxVOOverride, _bPlayVO), ref m_coroSignalAlarmArea);
		}
	}

	private IEnumerator signalAlarmDelayed(MiCharacter _charTarget, Transform _transTarget, AlarmSystem.AlarmType _eAlarmType, MiSfxInfo _sfxVOOverride = null, bool _bPlayVO = true)
	{
		Vector3 v3TargetPosBeforeDelay = _transTarget.position;
		Vector3 v3SourcePosBeforeDelay = m_character.trans.position;
		float fWaitUntilSignalAlarm = MiTime.time + DifficultySettings.m_difficultySettingsCurrent.m_fDelaySignalAlarm;
		while (fWaitUntilSignalAlarm > MiTime.time)
		{
			yield return null;
		}
		AlarmSystem.AlarmCause alarmCause = AlarmSystem.AlarmCause.Other;
		if (m_data.m_charDetected != null)
		{
			alarmCause = ((m_data.m_charDetected is MiCharacterPlayer) ? AlarmSystem.AlarmCause.Player : AlarmSystem.AlarmCause.Corpse);
		}
		MiSingletonSaveMortal<AlarmSystem>.instance.signalAlarm(new AlarmTargetInfo(_charTarget, _transTarget, v3TargetPosBeforeDelay, m_charNPC, m_charNPC.trans, v3SourcePosBeforeDelay, _eAlarmType, alarmCause));
		if ((bool)_sfxVOOverride)
		{
			m_charNPC.m_charSound.stopAndClearSfxQueue();
			m_charNPC.m_charSound.playSfxQueued(_sfxVOOverride);
		}
		else if (_bPlayVO)
		{
			m_charNPC.m_charSound.signalAlarm(_eAlarmType);
		}
	}

	public MiTreeLeaf.Result _bIsInAlarmSquad(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(alarmSquad != null);
	}

	public MiTreeLeaf.Result _bIsAlarmSquadLeader(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(alarmSquad != null && alarmSquad.bIsLeader(m_charNPC));
	}

	public MiTreeLeaf.Result _bAlarmSquadIsAreaAlarmed(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(alarmSquad.bIsAreaAlarmed());
	}

	public MiTreeLeaf.Result _bIsOnlyMemberOfSquad(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(alarmSquad != null && alarmSquad.iMembers == 1);
	}

	public MiTreeLeaf.Result _bIsAlarmed(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bIsAlarmed());
	}

	public bool bIsAlarmed()
	{
		return bIsAlarmedLocal() || MiSingletonSaveMortal<AlarmSystem>.instance.bIsAreaAlarm(m_charNPC) || MiSingletonSaveMortal<AlarmSystem>.instance.bIsGlobalAlarm() || ((bool)alarmSquad && alarmSquad.bIsAreaAlarmed());
	}

	private bool bIsAlarmedLocal()
	{
		return m_data.m_fLocalAlarmUntil > MiTime.time;
	}

	public MiTreeLeaf.Result _bIsAlarmedArea(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiSingletonSaveMortal<AlarmSystem>.instance.bIsAreaAlarm(m_charNPC));
	}

	public MiTreeLeaf.Result _bIsAlarmedGlobal(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiSingletonSaveMortal<AlarmSystem>.instance.bIsGlobalAlarm());
	}

	public MiTreeLeaf.Result _bNewLocalAlarmSet(bool _bInit)
	{
		if (m_data.m_fLocalAlarmFreshUntil > MiTime.time)
		{
			m_data.m_fLocalAlarmFreshUntil = 0f;
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public void setLocalAlarm(AlarmTargetInfo _targetInfo, float _fAlarmDuration)
	{
		m_data.m_alarmTargetInfo = _targetInfo;
		m_data.m_fLocalAlarmUntil = MiTime.time + _fAlarmDuration;
		m_data.m_fLocalAlarmFreshUntil = MiTime.time + m_fLocalAlarmFreshDuration;
		m_charNPC.m_aiMemory.addAtPosition(_targetInfo.transTarget, _targetInfo.v3TargetPos);
		for (int i = 0; i < _targetInfo.liTransTargetCollection.Count; i++)
		{
			m_charNPC.m_aiMemory.add(_targetInfo.liTransTargetCollection[i]);
		}
	}

	public MiTreeLeaf.Result _bCurrentTargetIsAlarmTarget(bool _bInit)
	{
		MiCharacterTarget miCharacterTarget = m_character.getMiCharacterTarget();
		if (miCharacterTarget == null)
		{
			return MiTreeLeaf.Result.Failure;
		}
		if ((bool)miCharacterTarget.transTarget && miCharacterTarget.transTarget == m_data.m_alarmTargetInfo.transTarget)
		{
			return MiTreeLeaf.Result.Success;
		}
		Vector3 v3TargetPos = miCharacterTarget.m_v3TargetPos;
		if (m_data.m_alarmTargetInfo == null)
		{
			return MiTreeLeaf.Result.Failure;
		}
		bool flag = MiMath.bLossyCompare(v3TargetPos, m_data.m_alarmTargetInfo.v3TargetPos, 0.1f);
		if (flag)
		{
			flag = !MiSingletonSaveMortal<AIMemoryEvents>.instance.bIgnoreTransform(m_data.m_alarmTargetInfo.transTarget);
		}
		return MiTreeLeaf.boolToResult(flag);
	}

	public MiTreeLeaf.Result _triggerCmdOnAlarm(bool _bInit)
	{
		if (m_charNPC.m_cmdOnAlarm != null)
		{
			m_charNPC.m_cmdOnAlarm.trigger();
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bWasAlarmedLastFrame(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_bWasAlarmedLastFrame);
	}

	public MiTreeLeaf.Result _setWasAlarmedLastFrameTrue(bool _bInit)
	{
		m_bWasAlarmedLastFrame = true;
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setWasAlarmedLastFrameFalse(bool _bInit)
	{
		m_bWasAlarmedLastFrame = false;
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _makeNoiseAlarm(bool _bInit)
	{
		makeNoise(NoiseDetection.NoiseType.Whistle);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _makeNoiseAlmostAlarm(bool _bInit)
	{
		makeNoise(NoiseDetection.NoiseType.AlmostAlarm);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _makeNoiseSuspicious(bool _bInit)
	{
		makeNoise(NoiseDetection.NoiseType.Suspicious);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _makeNoise(bool _bInit)
	{
		makeNoise(NoiseDetection.NoiseType.Noise);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _makeNoiseSeeCorpse(bool _bInit)
	{
		MiCharacterTarget target = new MiCharacterTargetLookAt(m_data.m_v3TargetPos, m_fLookAtDangerDurationStatic);
		m_character.m_noiseEmitter.emitOneShot(m_noiseSettingsSeeCorpse, new NoiseEmitter.SkillOnNoiseDetect(Skill.SkillType.LookAt, target, m_data.m_transTarget));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setNoiseSkillWakeUpNPC(bool _bInit)
	{
		m_skillNoise = new NoiseEmitter.SkillOnNoiseDetect(Skill.SkillType.WakeUpNPC, new MiCharacterTarget(m_data.m_charDetected, m_data.m_transTarget), m_data.m_transTarget);
		return MiTreeLeaf.Result.Success;
	}

	private void makeNoise(NoiseDetection.NoiseType _noiseType)
	{
		if (m_skillNoise == null)
		{
			makeNoise(_noiseType, new NoiseEmitter.SkillOnNoiseDetect(Skill.SkillType.Investigate, new MiCharacterTargetInvestigate(m_data.m_v3TargetPos, m_data.m_transTarget, MiCharacterMovementType.MovementType.Aiming, 0, null, null, _bInvestigateFar: true, _bInvestigateDoor: false, _bInvestigateAccident: false, _bInvestigateDanger: false, _bInvestigateMissing: false, null, 200f, 0f, 0f), m_data.m_transTarget));
		}
		else
		{
			makeNoise(_noiseType, m_skillNoise);
		}
	}

	private void makeNoise(NoiseDetection.NoiseType _noiseType, NoiseEmitter.SkillOnNoiseDetect _skillNoise)
	{
		if (m_fLastNoiseTime + m_fNoiseCooldown < MiTime.time)
		{
			m_fLastNoiseTime = MiTime.time;
			m_noiseSettingsGeneric.noiseType = _noiseType;
			m_character.m_charSound.makeNoise(_noiseType);
			m_character.m_noiseEmitter.emitOneShot(m_noiseSettingsGeneric, _skillNoise);
		}
	}

	public MiTreeLeaf.Result _bNoiseHasSkillOnDetect(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_data.m_skillOnDetectNoise != null);
	}

	public MiTreeLeaf.Result _bDoesNothing(bool _bInit)
	{
		if (m_charNPC.m_detectionOrientation._bHasFocus(_bInit) == MiTreeLeaf.Result.Success)
		{
			return MiTreeLeaf.Result.Failure;
		}
		if (bIsAlarmed())
		{
			return MiTreeLeaf.Result.Failure;
		}
		if (_bSkillIdleActive(_bInit) == MiTreeLeaf.Result.Success || m_character.m_skillHandler._bHasSkill(_bInit) == MiTreeLeaf.Result.Failure)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _bIsAlert(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bIsAlertState(AlertState.Alert));
	}

	public MiTreeLeaf.Result _setAlert(bool _bInit)
	{
		setAlertState(AlertState.Alert);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSuspicious(bool _bInit)
	{
		setAlertState(AlertState.Suspicious);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bIsSuspicious(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bIsAlertState(AlertState.Suspicious));
	}

	public MiTreeLeaf.Result _setIdle(bool _bInit)
	{
		setAlertState(AlertState.Idle);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bIsIdle(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bIsAlertState(AlertState.Idle));
	}

	public MiTreeLeaf.Result _setDistracted(bool _bInit)
	{
		setAlertState(AlertState.Distracted);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bIsDistracted(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bIsAlertState(AlertState.Distracted));
	}

	public bool bIsAlertState(AlertState _eState)
	{
		return eAlertState == _eState;
	}

	public void addDelegateOnAlertStateChangeTo(delAlertStateChange _del, AlertState _state)
	{
		if (m_dictAlertStateEventsChangeTo.ContainsKey(_state))
		{
			Dictionary<AlertState, delAlertStateChange> dictAlertStateEventsChangeTo;
			Dictionary<AlertState, delAlertStateChange> dictionary = (dictAlertStateEventsChangeTo = m_dictAlertStateEventsChangeTo);
			AlertState key;
			AlertState key2 = (key = _state);
			delAlertStateChange a = dictAlertStateEventsChangeTo[key];
			dictionary[key2] = (delAlertStateChange)Delegate.Combine(a, _del);
		}
		else
		{
			m_dictAlertStateEventsChangeTo.Add(_state, _del);
		}
	}

	public void addDelegateOnAlertStateChangeFrom(delAlertStateChange _del, AlertState _state)
	{
		if (m_dictAlertStateEventsChangeFrom.ContainsKey(_state))
		{
			Dictionary<AlertState, delAlertStateChange> dictAlertStateEventsChangeFrom;
			Dictionary<AlertState, delAlertStateChange> dictionary = (dictAlertStateEventsChangeFrom = m_dictAlertStateEventsChangeFrom);
			AlertState key;
			AlertState key2 = (key = _state);
			delAlertStateChange a = dictAlertStateEventsChangeFrom[key];
			dictionary[key2] = (delAlertStateChange)Delegate.Combine(a, _del);
		}
		else
		{
			m_dictAlertStateEventsChangeFrom.Add(_state, _del);
		}
	}

	private void setAlertState(AlertState _eState)
	{
		AlertState alertState = eAlertState;
		AlertState alertState2 = _eState;
		if (alertState != alertState2 && m_dictAlertStateEventsChangeFrom.ContainsKey(alertState))
		{
			m_dictAlertStateEventsChangeFrom[alertState](m_charNPC);
			m_dictAlertStateEventsChangeFrom.Remove(alertState);
		}
		if (alertState == AlertState.Distracted && alertState2 != AlertState.Distracted)
		{
			m_charNPC.endDistraction();
		}
		if (alertState != alertState2 || alertState2 == AlertState.Suspicious || alertState2 == AlertState.Alert)
		{
			m_data.m_eAlertState = alertState2;
			m_charNPC.m_detection.setAlertState(alertState2, m_bIsStatic);
		}
		else
		{
			alertState2 = alertState;
		}
		if (alertState != alertState2 && m_dictAlertStateEventsChangeTo.ContainsKey(alertState2))
		{
			m_dictAlertStateEventsChangeTo[alertState2](m_charNPC);
			m_dictAlertStateEventsChangeTo.Remove(alertState2);
		}
	}

	public MiTreeLeaf.Result _setSkillTargetOnly(bool _bInit)
	{
		m_character.skillCommand = new SkillCommand(null, new MiCharacterTarget(m_data.m_charDetected, m_data.m_transTarget));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillGunAttack(bool _bInit)
	{
		setSkill(Skill.SkillType.PlyGunAttack, new MiCharacterTarget(m_data.m_charDetected, m_data.m_charDetected.trans));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillDrawAttention(bool _bInit)
	{
		SkillDrawAttention.AttentionCause eAttentionCause = (m_data.m_charDetected.m_charHealth.bDead() ? SkillDrawAttention.AttentionCause.ScaryThing : SkillDrawAttention.AttentionCause.Corpse);
		setSkill(Skill.SkillType.DrawAttention, new MiCharacterTargetDrawAttention(m_data.m_charDetected, m_data.m_charDetected.trans, eAttentionCause), SkillSpreader.SpreadType.None);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillDrawAttentionToAccident(bool _bInit)
	{
		setSkill(Skill.SkillType.DrawAttention, new MiCharacterTargetDrawAttention(m_data.m_charDetected, m_data.m_charDetected.trans, SkillDrawAttention.AttentionCause.Accident), SkillSpreader.SpreadType.None);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillDrawAttentionAfterLookAt(bool _bInit)
	{
		SkillDrawAttention.AttentionCause eAttentionCause = (m_data.m_charDetected.m_charHealth.bDead() ? SkillDrawAttention.AttentionCause.ScaryThing : SkillDrawAttention.AttentionCause.Corpse);
		MiCharacterTarget miCharacterTarget = new MiCharacterTargetDrawAttention(m_data.m_charDetected, m_data.m_charDetected.trans, eAttentionCause);
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillDrawAttention;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillDrawAttention(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.DrawAttention, _arObjData[0] as MiCharacterTarget, SkillSpreader.SpreadType.None);
		_npc.m_ai.setMovementType(MiCharacterMovementType.MovementType.Feared);
		_npc.m_detection.addDataPreset(DetectionDataPresets.PresetName.Panic);
	}

	public MiTreeLeaf.Result _setSkillRunAway(bool _bInit)
	{
		m_character.skillCommand = null;
		(m_character.getSkill(Skill.SkillType.DrawAttention) as SkillDrawAttention).runAwayFrom(m_data.m_v3TargetPos);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setSkillRunAwayAfterLookAt(bool _bInit)
	{
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillRunAway;
		m_arObjGenericDataAfterLookAt = new object[1] { m_data.m_transTarget.position };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillRunAway(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.skillCommand = null;
		_npc.m_detection.addDataPreset(DetectionDataPresets.PresetName.Panic);
		(_npc.getSkill(Skill.SkillType.DrawAttention) as SkillDrawAttention).runAwayFrom((Vector3)_arObjData[0]);
	}

	public MiTreeLeaf.Result _setSkillFollowFootprintsAfterLookAt(bool _bInit)
	{
		MiCharacterTarget miCharacterTarget = new MiCharacterTarget(m_data.m_clickableDetected, m_data.m_clickableDetected.trans);
		m_fLookAtDuration = m_aiSettings.m_fDelayShort;
		m_delAfterLookAtGeneric = setSkillFollowFootprints;
		m_arObjGenericDataAfterLookAt = new object[1] { miCharacterTarget };
		return MiTreeLeaf.Result.Success;
	}

	private void setSkillFollowFootprints(MiCharacterNPC _npc, object[] _arObjData)
	{
		_npc.m_ai.setSkill(Skill.SkillType.FollowFootprints, _arObjData[0] as MiCharacterTarget);
		_npc.m_ai.setMovementType(MiCharacterMovementType.MovementType.Run);
		_npc.m_detection.addDataPreset(DetectionDataPresets.PresetName.Investigate);
		_npc.m_ai._spreadSkill(_bInit: true);
		SkillSpreader activeSkillSpreader = m_charNPC.m_ai.getActiveSkillSpreader();
		if (activeSkillSpreader != null)
		{
			List<MiCharacterNPC> nPCs = activeSkillSpreader.spreadHolder.getNPCs();
			for (int i = 0; i < nPCs.Count; i++)
			{
				nPCs[i].m_detection.ignoreType(typeof(Footprint));
			}
		}
	}

	public MiTreeLeaf.Result _setSkillWakeUpNPC(bool _bInit)
	{
		setSkill(Skill.SkillType.WakeUpNPC, new MiCharacterTarget(m_data.m_charDetected, m_data.m_transObjDetected), SkillSpreader.SpreadType.Follow);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _handleSkillOnNoise(bool _bInit)
	{
		m_fLookAtDuration = m_aiSettings.m_fDelayLong;
		m_delAfterLookAtGeneric = setSkillNoiseOnDetect;
		m_arObjGenericDataAfterLookAt = new object[1] { m_data.m_skillOnDetectNoise };
		return MiTreeLeaf.Result.Success;
	}

	private bool setSkill(Skill.SkillType _skillType, MiCharacterTarget _target, SkillSpreader.SpreadType _spreadType = SkillSpreader.SpreadType.Spread, bool _bSetProcessed = false)
	{
		if (bSkillIgnored(_skillType) || (m_character.m_charHealth.hasCondition(MiCharacterHealth.HealthCondition.Stunned) && !bSkillAllowedWhenStunned(_skillType)))
		{
			return false;
		}
		if (_skillType == Skill.SkillType.LookAt && m_fLookAtDuration.HasValue && (m_delAfterLookAt != null || m_delAfterLookAtGeneric != null))
		{
			MiCharacterTargetLookAt miCharacterTargetLookAt = _target as MiCharacterTargetLookAt;
			miCharacterTargetLookAt.m_fLookAtDuration = m_fLookAtDuration.Value;
			miCharacterTargetLookAt.m_delAfterLookAt = m_delAfterLookAt;
			miCharacterTargetLookAt.m_delAfterLookAtGeneric = m_delAfterLookAtGeneric;
			miCharacterTargetLookAt.m_arObjGenericData = m_arObjGenericDataAfterLookAt;
			m_fLookAtDuration = null;
			m_delAfterLookAt = null;
			m_delAfterLookAtGeneric = null;
			m_arObjGenericDataAfterLookAt = null;
		}
		m_skillTypeToSpread = _skillType;
		m_targetToSpread = _target;
		m_bSetProcessedOnSpread = _bSetProcessed;
		m_spreadType = _spreadType;
		m_skillTypeSetThisFrame = _skillType;
		m_v3TargetPositionSetThisFrame = _target.m_v3TargetPos;
		m_character.skillCommand = new SkillCommand(m_character.getSkill(_skillType), _target.copy());
		if (m_bSetProcessedOnSpread)
		{
			m_character.skillCommand.eState = SkillCommand.State.Process;
		}
		return true;
	}

	public void setSkillExtern(Skill.SkillType _skillType, MiCharacterTarget _target, SkillSpreader.SpreadType _spreadType = SkillSpreader.SpreadType.Spread, bool _bPauseRoutines = true)
	{
		if (_bPauseRoutines)
		{
			m_charNPC.routineSystem.pauseRoutines();
		}
		setSkill(_skillType, _target, _spreadType);
		_spreadSkill(_bInit: true);
	}

	private void setSkillNoiseOnDetect(MiCharacterNPC _npc, object[] _arObjData)
	{
		NoiseEmitter.SkillOnNoiseDetect skillOnNoiseDetect = _arObjData[0] as NoiseEmitter.SkillOnNoiseDetect;
		_npc.m_ai.setSkill(skillOnNoiseDetect.m_skillType, skillOnNoiseDetect.m_target);
		if (skillOnNoiseDetect.m_moveType.HasValue)
		{
			_npc.m_ai.setMovementType(skillOnNoiseDetect.m_moveType.Value);
		}
	}

	public bool bSkillIgnored(Skill.SkillType _skillType)
	{
		return m_liSkillsIgnore.Contains(_skillType);
	}

	public bool bSkillAllowedWhenStunned(Skill.SkillType _skillType)
	{
		return m_liSkillsAllowedWhenStunned.Contains(_skillType);
	}

	public SkillSpreader getActiveSkillSpreader()
	{
		if (m_skillSpreaderRAction != null)
		{
			return m_skillSpreaderRAction;
		}
		if (m_skillSpreaderFormation != null)
		{
			return m_skillSpreaderFormation;
		}
		return null;
	}

	public MiTreeLeaf.Result _spreadSkill(bool _bInit)
	{
		if (m_spreadType != 0)
		{
			trySpreadSkill(m_skillTypeToSpread, m_targetToSpread, m_spreadType, m_bSetProcessedOnSpread);
			m_targetToSpread = null;
			m_spreadType = SkillSpreader.SpreadType.None;
		}
		return MiTreeLeaf.Result.Success;
	}

	public SkillSpreader trySpreadSkill(Skill.SkillType _skillType, MiCharacterTarget _target, SkillSpreader.SpreadType _spreadType, bool _bRegisterFocus = false)
	{
		if (m_skillSpreaderRAction != null)
		{
			m_skillSpreaderRAction.spreadSkill(m_charNPC, _skillType, m_data, _target, _spreadType, _bRegisterFocus, _bNullSkillOnRoutineResume: true);
			return m_skillSpreaderRAction;
		}
		if (m_skillSpreaderFormation != null)
		{
			m_skillSpreaderFormation.spreadSkill(m_charNPC, _skillType, m_data, _target, _spreadType, _bRegisterFocus);
			return m_skillSpreaderFormation;
		}
		return null;
	}

	public void receiveSkillSpread(Skill.SkillType _skillType, Data _data, MiCharacterTarget _target, MiCharacterMovementType.MovementType _moveType = MiCharacterMovementType.MovementType.Walk)
	{
		bool flag = m_character.skillCommand != null;
		bool flag2 = _skillType == Skill.SkillType.Wait && ((flag && m_charNPC.routineSystem.bPaused()) || bSkillIgnored(_skillType));
		bool flag3 = flag && _skillType == m_skillTypeSetThisFrame && MiMath.bLossyCompare(_target.m_v3TargetPos, m_v3TargetPositionSetThisFrame, 0.05f);
		bool flag4 = m_character.m_charHealth.hasCondition(MiCharacterHealth.HealthCondition.Stunned);
		if (!flag2 && !flag3 && !flag4 && _data.m_iDetectionActive <= m_data.m_iDetectionActive)
		{
			MiCharacterTarget charTarget = _target.copy();
			Skill skill = m_character.getSkill(_skillType);
			if (skill == null)
			{
				skill = m_character.getSkill(Skill.SkillType.Wait);
				charTarget = new MiCharacterTargetLookAt(_target.m_v3TargetPos, 0f);
			}
			m_charNPC.routineSystem.pauseRoutines();
			setData(_data);
			m_character.skillCommand = new SkillCommand(skill, charTarget);
			setMovementType(_moveType);
		}
	}

	public MiTreeLeaf.Result _bActiveInvestigateHasSameDetectionIndex(bool _bInit)
	{
		if (m_character.skillCommandActive != null && m_character.skillCommandActive.m_MiCharacterTarget is MiCharacterTargetInvestigate miCharacterTargetInvestigate && miCharacterTargetInvestigate.m_iDetectionIndex == m_data.m_iDetectionCurrent)
		{
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _bSkillIdleActive(bool _bInit)
	{
		Skill.SkillType eSkillTypeCurrent = m_character.eSkillTypeCurrent;
		Skill.SkillType[] arSkillsIdle = m_aiSettings.m_arSkillsIdle;
		for (int i = 0; i < arSkillsIdle.Length; i++)
		{
			if (eSkillTypeCurrent == arSkillsIdle[i])
			{
				return MiTreeLeaf.Result.Success;
			}
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _bCharIsAlreadyTarget(bool _bInit)
	{
		ClickableObject objRef = m_charNPC.m_detection.focusedObject.objRef;
		MiCharacterTarget miCharacterTarget = m_character.getMiCharacterTarget();
		if (objRef == null || miCharacterTarget == null)
		{
			return MiTreeLeaf.Result.Failure;
		}
		return MiTreeLeaf.boolToResult(objRef == miCharacterTarget.m_MiBaseComponent);
	}

	public MiTreeLeaf.Result _bFollowingFootprints(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_character.bSkillActive(Skill.SkillType.FollowFootprints));
	}

	public MiTreeLeaf.Result _bActiveSkillUse(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_character.bSkillActive(Skill.SkillType.Use) || m_character.bSkillActive(Skill.SkillType.UseLightSource));
	}

	public MiTreeLeaf.Result _bActiveSkillUseInSkillSpreader(bool _bInit)
	{
		SkillSpreader activeSkillSpreader = getActiveSkillSpreader();
		if (activeSkillSpreader != null)
		{
			List<MiCharacterNPC> nPCs = activeSkillSpreader.spreadHolder.getNPCs();
			for (int i = 0; i < nPCs.Count; i++)
			{
				MiCharacterNPC miCharacterNPC = nPCs[i];
				if (miCharacterNPC.bSkillActive(Skill.SkillType.Use) || miCharacterNPC.bSkillActive(Skill.SkillType.UseLightSource))
				{
					return MiTreeLeaf.Result.Success;
				}
			}
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _bActiveSkillInvestigate(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_character.bSkillActive(Skill.SkillType.Investigate));
	}

	public MiTreeLeaf.Result _bIsStatic(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_bIsStatic);
	}

	public MiTreeLeaf.Result _bIsSamurai(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_character.m_eCharacter == MiCharacter.CharacterType.Samurai);
	}

	public MiTreeLeaf.Result _bIsStaticOrSamurai(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_bIsStatic || MiCharacter.bIsSamurai(m_character.m_eCharacter));
	}

	public MiTreeLeaf.Result _setAniStateMoving(bool _bInit)
	{
		m_character.m_charVis.m_charAnimation.resetMoving();
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setRunMode(bool _bInit)
	{
		setMovementType(MiCharacterMovementType.MovementType.Run);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setWalkMode(bool _bInit)
	{
		setMovementType(MiCharacterMovementType.MovementType.Walk);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setAimingMode(bool _bInit)
	{
		setMovementType(MiCharacterMovementType.MovementType.Aiming);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _setFearedMode(bool _bInit)
	{
		setMovementType(MiCharacterMovementType.MovementType.Feared);
		return MiTreeLeaf.Result.Success;
	}

	public void setMovementType(MiCharacterMovementType.MovementType _eMoveType)
	{
		if (m_character.m_charMovementType.eMovementType != MiCharacterMovementType.MovementType.Stunned)
		{
			if (_eMoveType == MiCharacterMovementType.MovementType.Stunned)
			{
				m_movementTypeAfterStun = m_charNPC.m_charMovementType.eMovementType;
				m_character.m_navMeshAgent.speed = 0f;
			}
			m_charNPC.m_charVis.m_charAnimation.resetMoving();
			m_charNPC.m_charMovementType.setMovementType(_eMoveType);
		}
		else
		{
			m_movementTypeAfterStun = _eMoveType;
		}
	}

	public void setMovementAfterStun()
	{
		m_charNPC.m_charMovementType.setMovementType(m_movementTypeAfterStun);
	}

	public MiTreeLeaf.Result _stopMovement(bool _bInit)
	{
		if (_bInit)
		{
			if (m_character.m_navMeshAgent.hasPath)
			{
				m_character.m_charSound.playStopMovement(m_character.m_charMovementType.eMovementType);
			}
			m_character.m_miMovement.deletePath();
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bIsInDoors(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_character.m_charMovementType.eActionType == MiCharacterMovementType.ActionType.Door);
	}

	public void setBlockAI(bool _bState, AIBlockReason _reason)
	{
		if (!m_character.bIsHostile())
		{
			_bState = true;
		}
		if (m_varLockAIBlocked.trySetVariable(_bState, (int)_reason))
		{
			m_bAIIsBlocked = _bState;
		}
	}

	public bool bAIIsBlocked()
	{
		return m_bAIIsBlocked;
	}

	public MiTreeLeaf.Result _unblockAI(bool _bInit)
	{
		setBlockAI(_bState: false, AIBlockReason.Skill);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bCutsceneActive(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiCamHandler.bCutsceneMode);
	}

	public MiTreeLeaf.Result _bAIBlockedByRoutine(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bAIIsBlocked());
	}

	public MiTreeLeaf.Result _bRoutinesPaused(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_charNPC.routineSystem.bPaused());
	}

	public MiTreeLeaf.Result _pauseRoutines(bool _bInit)
	{
		m_charNPC.routineSystem.pauseRoutines();
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _unpauseRoutines(bool _bInit)
	{
		if (!bRoutineBlocked)
		{
			m_charNPC.routineSystem.unPauseRoutines();
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bAnimationActionRunning(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_charNPC.routineSystem.bAnimationActionRunning());
	}

	public override void serializeMi(ref Dictionary<string, object> dStringObj)
	{
		if (!SaveLoadSceneManager.bRealNull(m_aiSettings))
		{
			dStringObj.Add("m_aiSettings", SaveLoadSceneManager.iRefToID(m_aiSettings));
		}
		if (!SaveLoadSceneManager.bRealNull(m_detectionIndexBuffer))
		{
			dStringObj.Add("m_detectionIndexBuffer", SaveLoadSceneManager.iRefToID(m_detectionIndexBuffer));
		}
		if (m_bIsStatic)
		{
			dStringObj.Add("m_bIsStatic", m_bIsStatic);
		}
		if (!SaveLoadSceneManager.bRealNull(m_data))
		{
			dStringObj.Add("m_data", SaveLoadSceneManager.iRefToID(m_data));
		}
		if (m_fDetectionDelayFinished != -1f)
		{
			dStringObj.Add("m_fDetectionDelayFinished", m_fDetectionDelayFinished);
		}
		if (m_fDetectionDelayCooldownFinished != -1f)
		{
			dStringObj.Add("m_fDetectionDelayCooldownFinished", m_fDetectionDelayCooldownFinished);
		}
		dStringObj.Add("m_fDelayFinished", m_fDelayFinished);
		if (m_fLookAtDangerDurationStatic != 4f)
		{
			dStringObj.Add("m_fLookAtDangerDurationStatic", m_fLookAtDangerDurationStatic);
		}
		if (m_fLookAtDistractionDurationStatic != 2f)
		{
			dStringObj.Add("m_fLookAtDistractionDurationStatic", m_fLookAtDistractionDurationStatic);
		}
		if (!SaveLoadSceneManager.bRealNull(m_dataCoverLadder))
		{
			dStringObj.Add("m_dataCoverLadder", SaveLoadSceneManager.iRefToID(m_dataCoverLadder));
		}
		if (m_bDistractable)
		{
			dStringObj.Add("m_bDistractable", m_bDistractable);
		}
		if (!SaveLoadSceneManager.bRealNull(m_transThrowStart))
		{
			dStringObj.Add("m_transThrowStart", SaveLoadSceneManager.iRefToID(m_transThrowStart));
		}
		if (!SaveLoadSceneManager.bRealNull(m_transThrowEnd))
		{
			dStringObj.Add("m_transThrowEnd", SaveLoadSceneManager.iRefToID(m_transThrowEnd));
		}
		if (m_iCanSignalAlarmTypes != 22)
		{
			dStringObj.Add("m_iCanSignalAlarmTypes", m_iCanSignalAlarmTypes);
		}
		if (!SaveLoadSceneManager.bRealNull(m_coroAlarmWhenInRange))
		{
			dStringObj.Add("m_coroAlarmWhenInRange", SaveLoadSceneManager.iRefToID(m_coroAlarmWhenInRange));
		}
		if (!SaveLoadSceneManager.bRealNull(m_coroSignalAlarmLocal))
		{
			dStringObj.Add("m_coroSignalAlarmLocal", SaveLoadSceneManager.iRefToID(m_coroSignalAlarmLocal));
		}
		if (!SaveLoadSceneManager.bRealNull(m_coroSignalAlarmArea))
		{
			dStringObj.Add("m_coroSignalAlarmArea", SaveLoadSceneManager.iRefToID(m_coroSignalAlarmArea));
		}
		if (m_fStaticAlarmCooldownDuration != 5f)
		{
			dStringObj.Add("m_fStaticAlarmCooldownDuration", m_fStaticAlarmCooldownDuration);
		}
		if (m_fTimeLocalAlarmCooldownReady != 0f)
		{
			dStringObj.Add("m_fTimeLocalAlarmCooldownReady", m_fTimeLocalAlarmCooldownReady);
		}
		if (m_fLocalAlarmFreshDuration != 1f)
		{
			dStringObj.Add("m_fLocalAlarmFreshDuration", m_fLocalAlarmFreshDuration);
		}
		if (m_bWasAlarmedLastFrame)
		{
			dStringObj.Add("m_bWasAlarmedLastFrame", m_bWasAlarmedLastFrame);
		}
		if (!SaveLoadSceneManager.bRealNull(m_noiseSettingsGeneric))
		{
			dStringObj.Add("m_noiseSettingsGeneric", SaveLoadSceneManager.iRefToID(m_noiseSettingsGeneric));
		}
		if (!SaveLoadSceneManager.bRealNull(m_noiseSettingsSeeCorpse))
		{
			dStringObj.Add("m_noiseSettingsSeeCorpse", SaveLoadSceneManager.iRefToID(m_noiseSettingsSeeCorpse));
		}
		if (!SaveLoadSceneManager.bRealNull(m_skillNoise))
		{
			dStringObj.Add("m_skillNoise", SaveLoadSceneManager.iRefToID(m_skillNoise));
		}
		if (m_fNoiseCooldown != 1f)
		{
			dStringObj.Add("m_fNoiseCooldown", m_fNoiseCooldown);
		}
		dStringObj.Add("m_fLastNoiseTime", m_fLastNoiseTime);
		if (!SaveLoadSceneManager.bRealNull(m_dictAlertStateEventsChangeTo))
		{
			dStringObj.Add("m_dictAlertStateEventsChangeTo", SaveLoadSceneManager.createIDDictValRef(m_dictAlertStateEventsChangeTo));
		}
		if (!SaveLoadSceneManager.bRealNull(m_dictAlertStateEventsChangeFrom))
		{
			dStringObj.Add("m_dictAlertStateEventsChangeFrom", SaveLoadSceneManager.createIDDictValRef(m_dictAlertStateEventsChangeFrom));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liSkillsIgnore))
		{
			dStringObj.Add("m_liSkillsIgnore", SaveLoadSceneManager.duplicateGenericListVal(m_liSkillsIgnore, typeof(Skill.SkillType)));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liSkillsAllowedWhenStunned))
		{
			dStringObj.Add("m_liSkillsAllowedWhenStunned", SaveLoadSceneManager.duplicateGenericListVal(m_liSkillsAllowedWhenStunned, typeof(Skill.SkillType)));
		}
		if (!SaveLoadSceneManager.bRealNull(m_delAfterLookAt))
		{
			dStringObj.Add("m_delAfterLookAt", SaveLoadSceneManager.iRefToID(m_delAfterLookAt));
		}
		if (!SaveLoadSceneManager.bRealNull(m_delAfterLookAtGeneric))
		{
			dStringObj.Add("m_delAfterLookAtGeneric", SaveLoadSceneManager.iRefToID(m_delAfterLookAtGeneric));
		}
		if (!SaveLoadSceneManager.bRealNull(m_arObjGenericDataAfterLookAt))
		{
			dStringObj.Add("m_arObjGenericDataAfterLookAt", SaveLoadSceneManager.arRefToIDs(m_arObjGenericDataAfterLookAt));
		}
		if (m_fLookAtDuration.HasValue)
		{
			dStringObj.Add("m_fLookAtDuration", m_fLookAtDuration);
		}
		if (!SaveLoadSceneManager.bRealNull(m_skillSpreaderFormation))
		{
			dStringObj.Add("m_skillSpreaderFormation", SaveLoadSceneManager.iRefToID(m_skillSpreaderFormation));
		}
		if (!SaveLoadSceneManager.bRealNull(m_skillSpreaderRAction))
		{
			dStringObj.Add("m_skillSpreaderRAction", SaveLoadSceneManager.iRefToID(m_skillSpreaderRAction));
		}
		dStringObj.Add("m_skillTypeToSpread", m_skillTypeToSpread);
		if (!SaveLoadSceneManager.bRealNull(m_targetToSpread))
		{
			dStringObj.Add("m_targetToSpread", SaveLoadSceneManager.iRefToID(m_targetToSpread));
		}
		if (m_bSetProcessedOnSpread)
		{
			dStringObj.Add("m_bSetProcessedOnSpread", m_bSetProcessedOnSpread);
		}
		dStringObj.Add("m_spreadType", m_spreadType);
		dStringObj.Add("m_skillTypeSetThisFrame", m_skillTypeSetThisFrame);
		dStringObj.Add("m_v3TargetPositionSetThisFrame", m_v3TargetPositionSetThisFrame);
		dStringObj.Add("m_movementTypeAfterStun", m_movementTypeAfterStun);
		if (!SaveLoadSceneManager.bRealNull(m_varLockAIBlocked))
		{
			dStringObj.Add("m_varLockAIBlocked", SaveLoadSceneManager.iRefToID(m_varLockAIBlocked));
		}
		if (m_bAIIsBlocked)
		{
			dStringObj.Add("m_bAIIsBlocked", m_bAIIsBlocked);
		}
		if (m_bRoutineBlocked)
		{
			dStringObj.Add("m_bRoutineBlocked", m_bRoutineBlocked);
		}
		base.serializeMi(ref dStringObj);
	}

	public override void deserializeMi(Dictionary<string, object> _dStringObj, Dictionary<int, UnityEngine.Object> _dIDsToObjects, Dictionary<int, UnityEngine.Object> _dIDsToAssets, Dictionary<int, object> _dIDsToClass)
	{
		if (_dStringObj.TryGetValue("m_aiSettings", out var value))
		{
			m_aiSettings = (AISettingsGlobal)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_detectionIndexBuffer", out value))
		{
			m_detectionIndexBuffer = (DetectionIndexBuffer)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bIsStatic", out value))
		{
			m_bIsStatic = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_data", out value))
		{
			m_data = (Data)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_fDetectionDelayFinished", out value))
		{
			m_fDetectionDelayFinished = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fDetectionDelayCooldownFinished", out value))
		{
			m_fDetectionDelayCooldownFinished = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fDelayFinished", out value))
		{
			m_fDelayFinished = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fLookAtDangerDurationStatic", out value))
		{
			m_fLookAtDangerDurationStatic = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fLookAtDistractionDurationStatic", out value))
		{
			m_fLookAtDistractionDurationStatic = (float)value;
		}
		if (_dStringObj.TryGetValue("m_dataCoverLadder", out value))
		{
			m_dataCoverLadder = (DetectionData)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bDistractable", out value))
		{
			m_bDistractable = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_transThrowStart", out value))
		{
			m_transThrowStart = (Transform)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_transThrowEnd", out value))
		{
			m_transThrowEnd = (Transform)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_iCanSignalAlarmTypes", out value))
		{
			m_iCanSignalAlarmTypes = (int)value;
		}
		if (_dStringObj.TryGetValue("m_coroAlarmWhenInRange", out value))
		{
			m_coroAlarmWhenInRange = (MiCoroutine)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_coroSignalAlarmLocal", out value))
		{
			m_coroSignalAlarmLocal = (MiCoroutine)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_coroSignalAlarmArea", out value))
		{
			m_coroSignalAlarmArea = (MiCoroutine)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_fStaticAlarmCooldownDuration", out value))
		{
			m_fStaticAlarmCooldownDuration = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fTimeLocalAlarmCooldownReady", out value))
		{
			m_fTimeLocalAlarmCooldownReady = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fLocalAlarmFreshDuration", out value))
		{
			m_fLocalAlarmFreshDuration = (float)value;
		}
		if (_dStringObj.TryGetValue("m_bWasAlarmedLastFrame", out value))
		{
			m_bWasAlarmedLastFrame = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_noiseSettingsGeneric", out value))
		{
			m_noiseSettingsGeneric = (NoiseEmitter.NoiseEmitterSettings)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_noiseSettingsSeeCorpse", out value))
		{
			m_noiseSettingsSeeCorpse = (NoiseEmitter.NoiseEmitterSettings)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_skillNoise", out value))
		{
			m_skillNoise = (NoiseEmitter.SkillOnNoiseDetect)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_fNoiseCooldown", out value))
		{
			m_fNoiseCooldown = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fLastNoiseTime", out value))
		{
			m_fLastNoiseTime = (float)value;
		}
		if (_dStringObj.TryGetValue("m_dictAlertStateEventsChangeTo", out value))
		{
			m_dictAlertStateEventsChangeTo = (Dictionary<AlertState, delAlertStateChange>)SaveLoadSceneManager.createDictionaryWithValRef(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(Dictionary<AlertState, delAlertStateChange>));
		}
		if (_dStringObj.TryGetValue("m_dictAlertStateEventsChangeFrom", out value))
		{
			m_dictAlertStateEventsChangeFrom = (Dictionary<AlertState, delAlertStateChange>)SaveLoadSceneManager.createDictionaryWithValRef(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(Dictionary<AlertState, delAlertStateChange>));
		}
		if (_dStringObj.TryGetValue("m_liSkillsIgnore", out value))
		{
			m_liSkillsIgnore = (List<Skill.SkillType>)SaveLoadSceneManager.duplicateGenericListVal(value, typeof(Skill.SkillType));
		}
		if (_dStringObj.TryGetValue("m_liSkillsAllowedWhenStunned", out value))
		{
			m_liSkillsAllowedWhenStunned = (List<Skill.SkillType>)SaveLoadSceneManager.duplicateGenericListVal(value, typeof(Skill.SkillType));
		}
		if (_dStringObj.TryGetValue("m_delAfterLookAt", out value))
		{
			m_delAfterLookAt = (SkillLookAt.delegateAfterLookAt)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_delAfterLookAtGeneric", out value))
		{
			m_delAfterLookAtGeneric = (SkillLookAt.delegateAfterLookAtGeneric)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_arObjGenericDataAfterLookAt", out value))
		{
			m_arObjGenericDataAfterLookAt = (object[])SaveLoadSceneManager.createArrayWithRefs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(object));
		}
		if (_dStringObj.TryGetValue("m_fLookAtDuration", out value))
		{
			m_fLookAtDuration = (float?)value;
		}
		if (_dStringObj.TryGetValue("m_skillSpreaderFormation", out value))
		{
			m_skillSpreaderFormation = (SkillSpreader)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_skillSpreaderRAction", out value))
		{
			m_skillSpreaderRAction = (SkillSpreader)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_skillTypeToSpread", out value))
		{
			m_skillTypeToSpread = (Skill.SkillType)(int)value;
		}
		if (_dStringObj.TryGetValue("m_targetToSpread", out value))
		{
			m_targetToSpread = (MiCharacterTarget)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bSetProcessedOnSpread", out value))
		{
			m_bSetProcessedOnSpread = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_spreadType", out value))
		{
			m_spreadType = (SkillSpreader.SpreadType)(int)value;
		}
		if (_dStringObj.TryGetValue("m_skillTypeSetThisFrame", out value))
		{
			m_skillTypeSetThisFrame = (Skill.SkillType)(int)value;
		}
		if (_dStringObj.TryGetValue("m_v3TargetPositionSetThisFrame", out value))
		{
			m_v3TargetPositionSetThisFrame = (Vector3)value;
		}
		if (_dStringObj.TryGetValue("m_movementTypeAfterStun", out value))
		{
			m_movementTypeAfterStun = (MiCharacterMovementType.MovementType)(int)value;
		}
		if (_dStringObj.TryGetValue("m_varLockAIBlocked", out value))
		{
			m_varLockAIBlocked = (VariableLockSystemBool)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bAIIsBlocked", out value))
		{
			m_bAIIsBlocked = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_bRoutineBlocked", out value))
		{
			m_bRoutineBlocked = (bool)value;
		}
		base.deserializeMi(_dStringObj, _dIDsToObjects, _dIDsToAssets, _dIDsToClass);
	}
}
