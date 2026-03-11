using System;
using System.Collections.Generic;
using UnityEngine;

[MiSerializeOrder(50)]
[ImplementsMiTreeLeafDelegate]
public class Detection : MiCharacterScopeUpdate
{
	public enum DetectionState
	{
		NotVisible,
		Registering,
		Visible,
		PreDetected,
		Detected
	}

	public enum VCModification
	{
		Hide,
		Keep,
		TryActivate
	}

	public enum DetectionDisableReason
	{
		PlayerState = 1,
		OffMeshLink = 2,
		Cutscene = 4,
		Misc = 8
	}

	public const float c_fRegisterTime = 0.1f;

	public const float c_fEyePosOffset = 0.1f;

	private const int c_iRayCastHitcount = 16;

	public const float c_fRayCastHeightOffset = 0.005f;

	private const float c_fDetectionRangeOffset = 0.1f;

	public DetectionDataSwitcher m_dataSwitcher;

	public DetectionDataPresets m_dataPresets;

	public ProximityDetection m_proximityDetection;

	public float m_fEyeHeight = 1.7f;

	private float m_fViewAngle = 60f;

	private float m_fViewRange = 27f;

	private float m_fCrawlDistance = 16f;

	private float m_fNoNormalVisionHeight;

	private float m_fNoVisionHeight;

	private float m_fVisionCapBottom;

	private float m_fYClampTop;

	private float m_fYClampBot;

	private float m_fVCEmptySpeed;

	public float m_fColliderHeight = 15f;

	public float m_fColliderOffsetY = -2.5f;

	[MiDontSave]
	private RaycastHit[] m_arRayHits = new RaycastHit[16];

	private Vector3 m_v3Forward;

	private Vector3 m_v3Position;

	private float m_fViewRangeSq;

	private TrackedObject m_focusedObject;

	private TrackedObject m_viewConeMarker;

	private Viewables.ResizeableEntryArray m_arViewablesActive = new Viewables.ResizeableEntryArray(1);

	public List<TrackedObject> m_liObjectsInRange;

	private List<TrackedObject> m_liObjectsVisible;

	private TrackedObjectsComparer m_trackedObjComparer;

	private AIHandler.AlertState m_eAlertState;

	private float m_fDetectionRange;

	public Footprint m_fpDetected;

	private CoroWait m_coroClearFpDelayed;

	private bool m_bVCFillSpeedOverride;

	private Viewables.Boundaries m_boundaries;

	private Viewables.delEntryOperation m_delOnAddEntry;

	private Viewables.delEntryOperation m_delOnRemoveEntry;

	private Viewables.delBoundaryCheck m_delBoundaryCheck;

	private static int s_iUpdateInterval = 2;

	private static int s_iUpdateIndex;

	private int m_iUpdateIndex;

	private Vector2 m_v2DirTarget = default(Vector2);

	public List<ClickableObject> m_liIgnoreObjects;

	private List<ClickableObject> m_liIgnoreObjectsDynamic = new List<ClickableObject>();

	[MiDontSave]
	private List<Type> m_liTypesIgnored = new List<Type>();

	private List<string> m_liStringTypesIgnored = new List<string>();

	private bool m_bRangeChanged;

	private float m_fDetectionRangeOnDecreaseInit;

	private bool m_bDetectionRangeMaxedThisFrame;

	private bool m_bDetectionRangeMaxedLastFrame;

	private int m_iCurrenctDetectionDisableReason;

	private bool m_bTryActivateVCOnActivate;

	private MiEditablePolyHidingSpot m_hidingSpotInsideOf;

	public TrackedObject focusedObject => m_focusedObject;

	public TrackedObject viewConeMarker => m_viewConeMarker;

	protected override void MiAwake()
	{
		m_liObjectsInRange = new List<TrackedObject>();
		m_liObjectsVisible = new List<TrackedObject>();
		m_trackedObjComparer = new TrackedObjectsComparer(this);
		m_coroClearFpDelayed = new CoroWait();
		m_delOnAddEntry = addClickableToTrack;
		m_delOnRemoveEntry = removeTracked;
		m_delBoundaryCheck = bBoundaryCheckViewable;
		m_boundaries = default(Viewables.Boundaries);
	}

	public override MiCharacterScope init(MiCharacter _character)
	{
		MiCharacterScope result = base.init(_character);
		m_dataPresets = MiSingletonSaveLazyMortal<DetectionDataPresetHandler>.instance.getPresetInstance(m_dataPresets, m_charNPC.m_detectionData);
		m_iUpdateIndex = s_iUpdateIndex++ % s_iUpdateInterval;
		return result;
	}

	protected override void MiStart()
	{
		restoreDefaultIgnoreTypes();
		if (m_charNPC.routineSystem.bPaused())
		{
			m_charNPC.routineSystem.addDelOnResume(m_charNPC, removeAllPresets);
		}
		else
		{
			m_charNPC.routineSystem.addDelOnPause(m_charNPC, addDelResume);
		}
	}

	private bool addDelResume(MiCharacterNPC _npc)
	{
		_npc.routineSystem.addDelOnResume(_npc, removeAllPresets);
		return true;
	}

	private bool removeAllPresets(MiCharacterNPC _npc)
	{
		_npc.m_detection._removeDataPresetAll(_bInit: true);
		_npc.routineSystem.addDelOnPause(_npc, addDelResume);
		return true;
	}

	public MiTreeLeaf.Result _bSomethingVisible(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_liObjectsVisible.Count > 0);
	}

	public MiTreeLeaf.Result _bSeesPlayer(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.Player, _bUseMemory: false);
	}

	public MiTreeLeaf.Result _bSeesNPC(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.NPC);
	}

	public MiTreeLeaf.Result _bSeesEnemy(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.Enemy);
	}

	public MiTreeLeaf.Result _bSeesDistraction(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.Distraction);
	}

	public MiTreeLeaf.Result _bSeesFootprint(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.Footprint);
	}

	public MiTreeLeaf.Result _bSeesLightSource(bool _bInit)
	{
		return bSeesObject(TrackedObject.Type.LightSource);
	}

	private MiTreeLeaf.Result bSeesObject(TrackedObject.Type _eObjType, bool _bUseMemory = true)
	{
		TrackedObject nearestVisibleObjOfType = getNearestVisibleObjOfType(_eObjType, _bUseMemory);
		if (nearestVisibleObjOfType != null)
		{
			if (m_focusedObject != nearestVisibleObjOfType)
			{
				m_focusedObject = nearestVisibleObjOfType;
			}
			changeDetectionRange(getDetectionRangeFillRate());
			MiSingletonSaveMortal<ViewCone>.instance.setDetectedObjectType(m_transThis, m_focusedObject.eType);
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public TrackedObject getNearestVisibleObjOfType(TrackedObject.Type _eObjType, bool _bUseMemory = true)
	{
		for (int i = 0; i < m_liObjectsVisible.Count; i++)
		{
			TrackedObject trackedObject = m_liObjectsVisible[i];
			bool flag = _bUseMemory && m_charNPC.m_aiMemory.bTransAtPositionInMemory(trackedObject.objRef.trans);
			if (trackedObject.eType == _eObjType && !flag)
			{
				return trackedObject;
			}
		}
		return null;
	}

	public MiTreeLeaf.Result _bDetectedObject(bool _bInit)
	{
		if (m_focusedObject != null && (m_focusedObject.state == DetectionState.Detected || (m_focusedObject.state == DetectionState.Visible && fDistanceToObj(m_focusedObject) <= m_fDetectionRange)))
		{
			if (m_focusedObject.eType == TrackedObject.Type.Player && m_focusedObject.viewable.preDetect(m_charNPC))
			{
				m_focusedObject.setState(DetectionState.PreDetected);
				return MiTreeLeaf.Result.Failure;
			}
			if (m_focusedObject.state != DetectionState.Detected)
			{
				detectObject(m_focusedObject);
			}
			if (m_focusedObject.eType == TrackedObject.Type.Footprint)
			{
				m_fpDetected = m_focusedObject.objRef as Footprint;
			}
			if (m_bVCFillSpeedOverride)
			{
				m_bVCFillSpeedOverride = false;
			}
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _addDataPresetRelaxed(bool _bInit)
	{
		addDataPreset(DetectionDataPresets.PresetName.Relaxed);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _addDataPresetStandard(bool _bInit)
	{
		addDataPreset(DetectionDataPresets.PresetName.Standard);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _addDataPresetPanic(bool _bInit)
	{
		addDataPreset(DetectionDataPresets.PresetName.Panic);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _addDataPresetInvestigate(bool _bInit)
	{
		addDataPreset(DetectionDataPresets.PresetName.Investigate);
		return MiTreeLeaf.Result.Success;
	}

	public void addDataPreset(DetectionDataPresets.PresetName _preset, bool _bForced = false)
	{
		DetectionData data = m_dataPresets.getData(_preset);
		data.copyValues(m_charNPC.m_detection.m_dataSwitcher.getActive(), DetectionData.DataSet.Height);
		if (_bForced)
		{
			if (!m_dataSwitcher.add(data))
			{
				m_dataSwitcher.setDataActive(data, _bInstant: false);
			}
		}
		else
		{
			m_dataSwitcher.add(data);
		}
	}

	public MiTreeLeaf.Result _removeDataPresetRelaxed(bool _bInit)
	{
		removeDataPreset(DetectionDataPresets.PresetName.Relaxed);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _removeDataPresetStandard(bool _bInit)
	{
		removeDataPreset(DetectionDataPresets.PresetName.Standard);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _removeDataPresetPanic(bool _bInit)
	{
		removeDataPreset(DetectionDataPresets.PresetName.Panic);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _removeDataPresetInvestigate(bool _bInit)
	{
		removeDataPreset(DetectionDataPresets.PresetName.Investigate);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _removeDataPresetAll(bool _bInit)
	{
		removeDataPreset(DetectionDataPresets.PresetName.Relaxed);
		removeDataPreset(DetectionDataPresets.PresetName.Standard);
		removeDataPreset(DetectionDataPresets.PresetName.Panic);
		removeDataPreset(DetectionDataPresets.PresetName.Investigate);
		return MiTreeLeaf.Result.Success;
	}

	public void removeDataPreset(DetectionDataPresets.PresetName _preset)
	{
		m_dataSwitcher.remove(m_dataPresets.getData(_preset));
	}

	public MiTreeLeaf.Result _increaseDetectionRange(bool _bInit)
	{
		if (!bDetectionRangeMaxed())
		{
			changeDetectionRange(getDetectionRangeFillRate(), _bForced: true);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _decreaseDetectionRange(bool _bInit)
	{
		if (m_fDetectionRange > 0f)
		{
			changeDetectionRange(0f - m_fVCEmptySpeed);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _nullDetectionRange(bool _bInit)
	{
		m_fDetectionRange = 0f;
		MiSingletonSaveMortal<ViewCone>.instance.setDetectionRange(m_transThis, 0f);
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _activateViewCone(bool _bInit)
	{
		if (!MiSingletonSaveMortal<MiPlayerInput>.instance.m_freezeState.bIsActive)
		{
			MiSingletonSaveMortal<ViewConeInput>.instance.activateVCExtern(m_charNPC, _bForced: true);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _forceViewCone(bool _bInit)
	{
		if (!MiSingletonSaveMortal<MiPlayerInput>.instance.m_freezeState.bIsActive)
		{
			MiSingletonSaveMortal<ViewConeInput>.instance.activateVCExtern(m_charNPC, _bForced: true, _bPlayerInput: true);
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _bViewConeActive(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiSingletonSaveMortal<ViewCone>.instance.bActive(m_transThis));
	}

	public MiTreeLeaf.Result _setVCFillSpeedSeeDying(bool _bInit)
	{
		if (!m_bVCFillSpeedOverride)
		{
			m_bVCFillSpeedOverride = true;
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _unIgnoreTanuki(bool _bInit)
	{
		unIgnoreType(typeof(MiCharacterTanuki));
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _ignoreFootprints(bool _bInit)
	{
		ignoreType(typeof(Footprint));
		return MiTreeLeaf.Result.Success;
	}

	public override bool bDoMiUpdate(MiUpdateHandler.UpdateType _type)
	{
		return m_bMiEnabled && m_iCurrenctDetectionDisableReason == 0 && !MiTime.sPaused && !SaveLoadSceneManager.bProcessing && !SceneSettings.bCutscene && (Time.frameCount % s_iUpdateInterval == m_iUpdateIndex || m_focusedObject != null);
	}

	public override void MiUpdate(MiUpdateHandler.UpdateType _type)
	{
		m_v3Forward = m_transThis.forward;
		m_v3Position = m_transThis.position;
		cleanup();
		updateViewables();
		updateVisibility();
		detectObjects();
		updateViewCone();
	}

	private void cleanup()
	{
		if (m_bVCFillSpeedOverride && m_liObjectsVisible.Count == 0)
		{
			m_bVCFillSpeedOverride = false;
		}
		m_bDetectionRangeMaxedLastFrame = m_bDetectionRangeMaxedThisFrame;
		m_bDetectionRangeMaxedThisFrame = false;
		if (m_focusedObject != null && m_focusedObject.state < DetectionState.Visible)
		{
			m_focusedObject = null;
		}
		if (m_viewConeMarker != null && m_viewConeMarker.state < DetectionState.Visible)
		{
			m_viewConeMarker = null;
		}
		removeNullEntries(m_liObjectsVisible);
		removeNullEntries(m_liObjectsInRange);
		for (int num = m_liObjectsVisible.Count - 1; num >= 0; num--)
		{
			if (!m_liObjectsInRange.Contains(m_liObjectsVisible[num]))
			{
				m_liObjectsVisible.RemoveAt(num);
			}
		}
	}

	private void removeNullEntries(List<TrackedObject> _list)
	{
		for (int num = _list.Count - 1; num >= 0; num--)
		{
			if (_list[num].transViewable == null)
			{
				_list.RemoveAt(num);
			}
		}
	}

	private void updateViewables()
	{
		m_boundaries.m_fRange = m_fViewRange;
		m_boundaries.m_fRangeSq = m_fViewRangeSq;
		MiSingletonSaveLazyMortal<Viewables>.instance.updateViewablesActiveInRange(m_v3Position, m_boundaries, m_delBoundaryCheck, m_arViewablesActive, m_delOnAddEntry, m_delOnRemoveEntry);
	}

	private bool bBoundaryCheckViewable(Viewables.Boundaries _bounds, Vector3 _v3QueryPos, Vector3 _v3ObjPos)
	{
		return MiMath.fSqDistanceXZ(_v3ObjPos, _v3QueryPos) <= _bounds.m_fRangeSq;
	}

	private void updateVisibility()
	{
		if (m_hidingSpotInsideOf != null)
		{
			m_hidingSpotInsideOf.setColliderInverted(_bValue: true);
		}
		for (int i = 0; i < m_liObjectsInRange.Count; i++)
		{
			TrackedObject trackedObject = m_liObjectsInRange[i];
			DetectionState detectionState = determineVisibility(trackedObject);
			if (trackedObject.bHiddenTracking)
			{
				trackedObject.setState(detectionState);
				continue;
			}
			if (trackedObject.state < DetectionState.PreDetected || detectionState != DetectionState.Visible)
			{
				trackedObject.setState(detectionState);
			}
			if (trackedObject.state >= DetectionState.Visible)
			{
				if (trackedObject.eType != TrackedObject.Type.ViewConeMarker)
				{
					if (!m_liObjectsVisible.Contains(trackedObject))
					{
						m_liObjectsVisible.Add(trackedObject);
					}
				}
				else
				{
					(trackedObject.objRef as ViewConeMarker).registerDetection(m_character as MiCharacterNPC);
					m_viewConeMarker = trackedObject;
				}
			}
			else
			{
				m_liObjectsVisible.Remove(trackedObject);
			}
		}
		if (m_hidingSpotInsideOf != null)
		{
			m_hidingSpotInsideOf.setColliderInverted(_bValue: false);
		}
		m_liObjectsVisible.Sort(m_trackedObjComparer);
	}

	private DetectionState determineVisibility(TrackedObject _obj)
	{
		if (!_obj.bIsRegistered)
		{
			return DetectionState.Registering;
		}
		bool flag = _obj.eType == TrackedObject.Type.Player && (_obj.objRef as MiCharacter).m_charHealth.bAlive();
		bool flag2 = _obj.eType == TrackedObject.Type.Distraction && _obj.objRef is MiCharacterTanuki;
		bool flag3 = (_obj.eType == TrackedObject.Type.Player || _obj.eType == TrackedObject.Type.Footprint) && m_character.iDisguiseLevel() < _obj.viewable.iDisguiseLevel();
		bool flag4 = !_obj.bHiddenTracking && m_charNPC.m_aiMemory.bTransAtPositionInMemory(_obj.objRef.trans);
		bool flag5 = _obj.eType == TrackedObject.Type.Footprint && m_fpDetected != null && _obj.objRef as Footprint <= m_fpDetected;
		bool flag6 = !_obj.bHiddenTracking && (m_liTypesIgnored.Contains(_obj.objRef.GetType()) || m_liIgnoreObjectsDynamic.Contains(_obj.objRef));
		if ((!_obj.viewable.bIsVisible() && (!_obj.bHiddenTracking || flag2)) || flag3 || (flag4 && !flag) || flag5 || flag6)
		{
			return DetectionState.NotVisible;
		}
		Vector3 position = _obj.transViewable.position;
		Vector3 eyePos = getEyePos(0f);
		float num = MiMath.fDistanceXZ(position, eyePos);
		if (num < 0.1f)
		{
			eyePos += m_v3Forward * -0.1f;
			num = MiMath.fDistanceXZ(position, eyePos);
		}
		bool flag7 = _obj.viewable.bIsCrawling();
		bool flag8 = !(_obj.viewable.lightSourceInsideOf != null) && num > m_fCrawlDistance && flag7;
		bool flag9 = flag7 && _obj.viewable.colHiding != null && _obj.viewable.colHiding != m_character.colHiding;
		bool flag10 = eyePos.y + m_fNoNormalVisionHeight < position.y;
		bool flag11 = flag7 && flag10;
		bool flag12 = eyePos.y + m_fNoVisionHeight < position.y;
		DetectionState result = DetectionState.NotVisible;
		if (bInYRange(position, m_fYClampBot, m_fYClampTop) && bInFOV(position, m_fViewAngle) && !flag8 && !flag11 && !flag9 && !flag12)
		{
			Vector3 vector = position;
			vector.y += _obj.viewable.fHeight() - 0.005f;
			Vector3 direction = vector - eyePos;
			float num2 = Vector2.Angle(new Vector2(1f, 0f), new Vector2(num, direction.y));
			float num3 = (m_fViewRange - 0.1f) / Mathf.Cos(num2 * ((float)Math.PI / 180f));
			float num4 = num3;
			if (Physics.Raycast(eyePos, direction, out var hitInfo, num3, 137216))
			{
				num4 = Vector3.Distance(eyePos, hitInfo.point);
			}
			bool flag13 = _obj.viewable.bViewableActive();
			if (_obj.bHiddenTracking && !flag13)
			{
				_obj.viewable.setViewableActive(_bState: true);
			}
			int num5 = Physics.RaycastNonAlloc(eyePos, direction, m_arRayHits, num3, 1024);
			for (int i = 0; i < num5; i++)
			{
				float num6 = Vector3.Distance(eyePos, m_arRayHits[i].point);
				if (num6 < num4 && m_arRayHits[i].collider.transform == _obj.transViewable)
				{
					result = DetectionState.Visible;
					break;
				}
			}
			if (_obj.bHiddenTracking && !flag13)
			{
				_obj.viewable.setViewableActive(_bState: false);
			}
		}
		return result;
	}

	private void detectObjects()
	{
		for (int i = 0; i < m_liObjectsVisible.Count; i++)
		{
			TrackedObject trackedObject = m_liObjectsVisible[i];
			if (trackedObject.state == DetectionState.Detected || m_charNPC.m_aiMemory.bTransAtPositionInMemory(trackedObject.objRef.trans))
			{
				continue;
			}
			bool flag = m_fDetectionRange >= fDistanceToObj(trackedObject) - 0.1f;
			if (!flag)
			{
				trackedObject.m_bWasVisibleOutsideOfDetectionRange = true;
			}
			if (trackedObject.eType == TrackedObject.Type.Player && flag && trackedObject.viewable.preDetect(m_charNPC))
			{
				trackedObject.setState(DetectionState.PreDetected);
			}
			else
			{
				if (!flag)
				{
					continue;
				}
				if (trackedObject.state == DetectionState.PreDetected)
				{
					trackedObject.setState(DetectionState.Visible);
					continue;
				}
				detectObject(trackedObject);
				if (trackedObject.eType != TrackedObject.Type.Distraction && trackedObject.eType != TrackedObject.Type.LightSource)
				{
					m_fDetectionRange = m_fViewRange;
				}
			}
		}
	}

	private void detectObject(TrackedObject _trackedObj)
	{
		if (_trackedObj.eType == TrackedObject.Type.Player && _trackedObj.viewable.iDisguiseLevel() < m_character.iDisguiseLevel() && !MiSingletonSaveMortal<DetectionEvents>.instance.bObjectDetected(_trackedObj.objRef))
		{
			_trackedObj.m_iDisguiseLevelOnDetect = _trackedObj.viewable.iDisguiseLevel();
			_trackedObj.viewable.detect(m_charNPC);
			MiSingletonSaveMortal<ViewConeInput>.instance.activateVCExtern(m_charNPC, _bForced: true, _bPlayerInput: true);
		}
		else if (_trackedObj.eType != TrackedObject.Type.Player)
		{
			_trackedObj.viewable.detect(m_charNPC);
		}
		_trackedObj.setState(DetectionState.Detected);
	}

	private void updateViewCone()
	{
		if (MiSingletonSaveMortal<ViewCone>.instance.bActive(m_transThis))
		{
			MiSingletonSaveMortal<ViewCone>.instance.setDetectionRange(m_transThis, m_fDetectionRange);
			MiSingletonSaveMortal<ViewCone>.instance.setDetectedObjectType(m_transThis, (m_focusedObject != null) ? m_focusedObject.eType : TrackedObject.Type.None);
			MiSingletonSaveMortal<ViewCone>.instance.setDistracted(m_transThis, m_eAlertState == AIHandler.AlertState.Distracted);
			MiSingletonSaveMortal<ViewCone>.instance.setDetected(m_transThis, m_eAlertState == AIHandler.AlertState.Alert);
		}
	}

	private Vector3 getEyePos(float _fBackwardsOffset = 0f)
	{
		Vector3 result = new Vector3(m_v3Position.x, m_v3Position.y + m_fEyeHeight, m_v3Position.z);
		if (_fBackwardsOffset == 0f)
		{
			return result;
		}
		return new Vector3(m_v3Position.x + m_v3Forward.x * (0f - _fBackwardsOffset), m_v3Position.y + m_fEyeHeight + m_v3Forward.y * (0f - _fBackwardsOffset), m_v3Position.z + m_v3Forward.z * (0f - _fBackwardsOffset));
	}

	public bool bInYRange(Vector3 _v3ObjPos)
	{
		return bInYRange(_v3ObjPos, m_fYClampBot, m_fYClampTop);
	}

	private bool bInYRange(Vector3 _v3ObjPos, float _fYBot, float _fYTop)
	{
		return _v3ObjPos.y > m_v3Position.y + _fYBot && _v3ObjPos.y < m_v3Position.y + _fYTop;
	}

	public bool bInFOV(Vector3 _v3Pos)
	{
		return bInFOV(_v3Pos, m_fViewAngle);
	}

	private bool bInFOV(Vector3 _v3Pos, float _fViewAngle)
	{
		Vector3 eyePos = getEyePos(0.1f);
		m_v2DirTarget.x = _v3Pos.x - eyePos.x;
		m_v2DirTarget.y = _v3Pos.z - eyePos.z;
		float num = Mathf.Acos(Mathf.Clamp(Vector2.Dot(MiMath.v2XZ(m_v3Forward), m_v2DirTarget.normalized), -1f, 1f)) * 57.29578f;
		return num <= _fViewAngle * 0.5f;
	}

	public float fDistanceToPos(Vector3 _v3Pos)
	{
		return MiMath.fDistanceXZ(_v3Pos, m_v3Position);
	}

	public float fDistanceToObj(TrackedObject _obj)
	{
		return fDistanceToPos(_obj.transViewable.position);
	}

	public bool bAboveViewHeight(Vector3 _pos)
	{
		return _pos.y > m_v3Position.y + m_fEyeHeight + m_fNoVisionHeight;
	}

	public void applyData(DetectionData _data)
	{
		m_fViewRange = _data.fViewRange;
		m_fCrawlDistance = _data.fCrawlDistance;
		m_fViewAngle = _data.fViewAngle;
		m_fVCEmptySpeed = _data.fEmptySpeed;
		m_fNoNormalVisionHeight = _data.fNoNormalVisionHeight;
		m_fNoVisionHeight = _data.fNoVisionHeight;
		m_fVisionCapBottom = _data.fVisionCapBottom;
		m_fViewRangeSq = m_fViewRange * m_fViewRange;
		if (m_eAlertState == AIHandler.AlertState.Suspicious || m_eAlertState == AIHandler.AlertState.Alert)
		{
			m_fDetectionRange = m_fViewRange;
		}
		setBounds();
	}

	public void setBounds()
	{
		float num = m_fNoVisionHeight + m_fVisionCapBottom;
		float num2 = m_fNoVisionHeight + m_fEyeHeight - num * 0.5f;
		m_fYClampBot = num2 - num / 2f;
		m_fYClampTop = num2 + num / 2f;
	}

	public void updateVCValues()
	{
		MiSingletonSaveMortal<ViewCone>.instance.updateValues(m_transThis, m_fViewRange, m_fCrawlDistance, m_fViewAngle, m_fNoNormalVisionHeight, m_fNoVisionHeight);
	}

	public void unIgnoreType(Type _typeRemove)
	{
		m_liTypesIgnored.Remove(_typeRemove);
	}

	public void ignoreType(Type _typeIgnore)
	{
		if (!m_liTypesIgnored.Contains(_typeIgnore))
		{
			m_liTypesIgnored.Add(_typeIgnore);
		}
	}

	public void ignoreObject(ClickableObject _clickable)
	{
		if (!m_liIgnoreObjectsDynamic.Contains(_clickable))
		{
			m_liIgnoreObjectsDynamic.Add(_clickable);
		}
	}

	public void unIgnoreObject(ClickableObject _clickable)
	{
		m_liIgnoreObjectsDynamic.Remove(_clickable);
	}

	public void clearIgnoreObjects()
	{
		m_liIgnoreObjectsDynamic.Clear();
	}

	public void restoreDefaultIgnoreTypes()
	{
		m_liTypesIgnored.Clear();
		for (int i = 0; i < m_liIgnoreObjects.Count; i++)
		{
			Type type = m_liIgnoreObjects[i].GetType();
			if (!m_liTypesIgnored.Contains(type))
			{
				m_liTypesIgnored.Add(type);
			}
		}
	}

	public override void OnSerialize()
	{
		m_arViewablesActive.removeNullEntries();
		m_arViewablesActive.trim();
		m_liStringTypesIgnored.Clear();
		for (int i = 0; i < m_liTypesIgnored.Count; i++)
		{
			m_liStringTypesIgnored.Add(ReflectionHelper.StringFromType(m_liTypesIgnored[i]));
		}
		base.OnSerialize();
	}

	public override void OnDeserialize()
	{
		for (int i = 0; i < m_liStringTypesIgnored.Count; i++)
		{
			m_liTypesIgnored.Add(JOMLWriter.typeFromStr(m_liStringTypesIgnored[i]));
		}
		base.OnDeserialize();
	}

	public void resetRangeChanged()
	{
		m_bRangeChanged = false;
	}

	private float getDetectionRangeFillRate()
	{
		if (m_bVCFillSpeedOverride)
		{
			return DifficultySettings.m_difficultySettingsCurrent.m_acFillSpeedSeeDying.Evaluate(m_fDetectionRange / m_fViewRange) * DifficultySettings.m_difficultySettingsCurrent.m_fVCFillSpeedSeeDying;
		}
		return DifficultySettings.m_difficultySettingsCurrent.m_acFillSpeed.Evaluate(m_fDetectionRange / m_fViewRange) * DifficultySettings.m_difficultySettingsCurrent.m_fVCFillSpeed;
	}

	private void changeDetectionRange(float _fSpeed, bool _bForced = false)
	{
		if (_bForced || !m_bRangeChanged)
		{
			m_fDetectionRange += Time.deltaTime * _fSpeed;
			m_fDetectionRange = Mathf.Clamp(m_fDetectionRange, 0f, m_fViewRange);
			if (m_fDetectionRange >= m_fViewRange)
			{
				m_bDetectionRangeMaxedThisFrame = true;
			}
			m_bRangeChanged = true;
			m_character.m_miTree.forceIterationNextFrame();
		}
	}

	public void decreaseDetectionRangeInit()
	{
		m_fDetectionRangeOnDecreaseInit = m_fDetectionRange;
	}

	public void decreaseDetectionRange(float _fDuration)
	{
		changeDetectionRange(m_fDetectionRangeOnDecreaseInit / _fDuration * -1f);
	}

	public void decreaseDetectionRange()
	{
		changeDetectionRange(0f - m_fVCEmptySpeed);
	}

	public bool bInDetectionRange(Vector3 _v3Pos)
	{
		return fDistanceToPos(_v3Pos) <= m_fDetectionRange;
	}

	public bool bDetectionRangeMaxed()
	{
		return m_bDetectionRangeMaxedThisFrame || m_bDetectionRangeMaxedLastFrame;
	}

	public void setAlertState(AIHandler.AlertState _eState, bool _bStaticNPC = false)
	{
		if (_eState == AIHandler.AlertState.Suspicious || _eState == AIHandler.AlertState.Alert)
		{
			m_fDetectionRange = m_fViewRange;
		}
		if (_eState == AIHandler.AlertState.Alert)
		{
			MiSingletonSaveMortal<ViewCone>.instance.setDetected(m_transThis, _bDetected: true);
		}
		else
		{
			MiSingletonSaveMortal<ViewCone>.instance.setDetected(m_transThis, _bDetected: false);
		}
		if (_eState == AIHandler.AlertState.Distracted)
		{
			MiSingletonSaveMortal<ViewCone>.instance.setDistracted(m_transThis, _bDistracted: true);
		}
		else
		{
			MiSingletonSaveMortal<ViewCone>.instance.setDistracted(m_transThis, _bDistracted: false);
		}
		if (_eState == AIHandler.AlertState.Idle)
		{
			if (_bStaticNPC)
			{
				m_coroClearFpDelayed.start(this, clearFootprint, 5f);
			}
			else
			{
				m_fpDetected = null;
			}
		}
		m_eAlertState = _eState;
	}

	public void clearFootprint()
	{
		m_fpDetected = null;
	}

	public void toggleViewCone(bool _bForced = false, bool _bPlayerInput = false)
	{
		if (!_bForced || !MiCamHandler.bCutsceneMode)
		{
			TrackedObject trackedObject = m_focusedObject;
			if (trackedObject == null && m_liObjectsVisible.Count > 0)
			{
				trackedObject = m_liObjectsVisible[0];
			}
			if (MiSingletonSaveMortal<ViewCone>.instance.showVC(m_transThis, trackedObject?.eType ?? TrackedObject.Type.None, m_fViewAngle, m_fViewRange, m_fCrawlDistance, m_fEyeHeight, m_fNoVisionHeight, m_fNoNormalVisionHeight, m_fVisionCapBottom, m_fDetectionRange, m_hidingSpotInsideOf, m_character.bIsFriendly(), _bForced, _bPlayerInput))
			{
				MiSingletonSaveMortal<ViewCone>.instance.setDistracted(m_transThis, m_eAlertState == AIHandler.AlertState.Distracted);
				MiSingletonSaveMortal<ViewCone>.instance.setDetected(m_transThis, m_eAlertState == AIHandler.AlertState.Alert);
				MiSingletonSaveLazyMortal<NPCCulling>.instance.overrideCulling(m_charNPC, _bVisible: true);
			}
		}
	}

	public void setActive(bool _bState, DetectionDisableReason _reason, VCModification _vcMode = VCModification.Hide)
	{
		bool flag = m_iCurrenctDetectionDisableReason == 0;
		if (_bState)
		{
			m_iCurrenctDetectionDisableReason = MiFlags.Remove(m_iCurrenctDetectionDisableReason, (int)_reason);
		}
		else
		{
			m_iCurrenctDetectionDisableReason = MiFlags.Add(m_iCurrenctDetectionDisableReason, (int)_reason);
		}
		bool flag2 = m_iCurrenctDetectionDisableReason == 0;
		if (flag != flag2)
		{
			setActive(flag2, _vcMode);
		}
		else if (!_bState && MiSingletonSaveMortal<ViewCone>.instance.bActive(m_transThis))
		{
			MiSingletonSaveMortal<ViewConeInput>.instance.deactivateVCExtern(m_charNPC);
		}
	}

	public void setActiveForced()
	{
		m_iCurrenctDetectionDisableReason = 0;
		setActive(_bState: true);
	}

	private void setActive(bool _bState, VCModification _vcMode = VCModification.Hide)
	{
		if (!_bState)
		{
			if (_vcMode != VCModification.Keep && MiSingletonSaveMortal<ViewCone>.instance.bActive(m_transThis))
			{
				m_bTryActivateVCOnActivate = true;
				MiSingletonSaveMortal<ViewConeInput>.instance.deactivateVCExtern(m_charNPC);
			}
			m_liObjectsVisible.Clear();
			m_liObjectsInRange.Clear();
			m_arViewablesActive.Count = 0;
			m_charNPC.m_detectionOrientation._endFocus(_bInit: true);
			m_focusedObject = null;
		}
		else if (m_bTryActivateVCOnActivate && _vcMode == VCModification.TryActivate)
		{
			MiSingletonSaveMortal<ViewConeInput>.instance.activateVCExtern(m_charNPC);
			m_bTryActivateVCOnActivate = false;
		}
		base.enabled = _bState;
		m_dataSwitcher.setActive(_bState);
		m_proximityDetection.enabled = _bState;
	}

	public void resetDetectionState(TrackedObject _obj)
	{
		_obj.setState(DetectionState.NotVisible);
		m_liObjectsVisible.Remove(_obj);
		if (m_focusedObject == _obj)
		{
			m_focusedObject = null;
		}
		m_charNPC.m_detectionOrientation.endFocus(_obj);
	}

	public void invertHidingSpotCollider(MiEditablePolyHidingSpot _hidingSpot)
	{
		m_hidingSpotInsideOf = _hidingSpot;
	}

	public void revertHidingSpotCollider()
	{
		m_hidingSpotInsideOf = null;
	}

	private void addClickableToTrack(Viewables.ViewableEntry _viewable)
	{
		if (m_character == _viewable.m_clickable)
		{
			return;
		}
		TrackedObject trackedObject = new TrackedObject(_viewable.m_clickable);
		if (trackedObject.eType == TrackedObject.Type.Footprint && m_fpDetected != null)
		{
			Footprint footprint = trackedObject.objRef as Footprint;
			bool flag = footprint.footprintTrail != m_fpDetected.footprintTrail;
			bool flag2 = footprint <= m_fpDetected;
			if (flag || flag2)
			{
				return;
			}
		}
		m_liObjectsInRange.Add(trackedObject);
		if (trackedObject.eType == TrackedObject.Type.Player)
		{
			MiSingletonSaveMortal<DetectionEvents>.instance.addNPCTrackedClickable(m_charNPC, trackedObject);
		}
	}

	public TrackedObject addObjectToTrack(ClickableObject _clickable)
	{
		TrackedObject trackedObject = new TrackedObject(_clickable, _bHidden: true);
		m_liObjectsInRange.Add(trackedObject);
		if (trackedObject.eType == TrackedObject.Type.Player)
		{
			MiSingletonSaveMortal<DetectionEvents>.instance.addNPCTrackedClickable(m_charNPC, trackedObject);
		}
		return trackedObject;
	}

	public void removeTrackedObject(TrackedObject _obj)
	{
		m_liObjectsInRange.Remove(_obj);
		MiSingletonSaveMortal<DetectionEvents>.instance.removeNPCTrackedClickable(m_charNPC, _obj);
	}

	private void removeTracked(Viewables.ViewableEntry _viewable)
	{
		for (int num = m_liObjectsInRange.Count - 1; num >= 0; num--)
		{
			TrackedObject trackedObject = m_liObjectsInRange[num];
			if (!trackedObject.bHiddenTracking && _viewable.m_transViewable == trackedObject.transViewable)
			{
				m_charNPC.m_detectionOrientation.endFocus(trackedObject);
				MiSingletonSaveMortal<DetectionEvents>.instance.removeNPCTrackedClickable(m_charNPC, trackedObject);
				m_liObjectsInRange.RemoveAt(num);
				m_liObjectsVisible.Remove(trackedObject);
				if (m_viewConeMarker != null && m_viewConeMarker.objRef == trackedObject.objRef)
				{
					m_viewConeMarker = null;
				}
				else if (m_focusedObject != null && m_focusedObject.objRef == trackedObject.objRef)
				{
					m_focusedObject = null;
				}
			}
		}
	}

	public override void serializeMi(ref Dictionary<string, object> dStringObj)
	{
		if (!SaveLoadSceneManager.bRealNull(m_dataSwitcher))
		{
			dStringObj.Add("m_dataSwitcher", SaveLoadSceneManager.iRefToID(m_dataSwitcher));
		}
		if (!SaveLoadSceneManager.bRealNull(m_dataPresets))
		{
			dStringObj.Add("m_dataPresets", SaveLoadSceneManager.iRefToID(m_dataPresets));
		}
		if (!SaveLoadSceneManager.bRealNull(m_proximityDetection))
		{
			dStringObj.Add("m_proximityDetection", SaveLoadSceneManager.iRefToID(m_proximityDetection));
		}
		if (m_fEyeHeight != 1.7f)
		{
			dStringObj.Add("m_fEyeHeight", m_fEyeHeight);
		}
		if (m_fViewAngle != 60f)
		{
			dStringObj.Add("m_fViewAngle", m_fViewAngle);
		}
		if (m_fViewRange != 27f)
		{
			dStringObj.Add("m_fViewRange", m_fViewRange);
		}
		if (m_fCrawlDistance != 16f)
		{
			dStringObj.Add("m_fCrawlDistance", m_fCrawlDistance);
		}
		dStringObj.Add("m_fNoNormalVisionHeight", m_fNoNormalVisionHeight);
		dStringObj.Add("m_fNoVisionHeight", m_fNoVisionHeight);
		dStringObj.Add("m_fVisionCapBottom", m_fVisionCapBottom);
		dStringObj.Add("m_fYClampTop", m_fYClampTop);
		dStringObj.Add("m_fYClampBot", m_fYClampBot);
		dStringObj.Add("m_fVCEmptySpeed", m_fVCEmptySpeed);
		if (m_fColliderHeight != 15f)
		{
			dStringObj.Add("m_fColliderHeight", m_fColliderHeight);
		}
		if (m_fColliderOffsetY != -2.5f)
		{
			dStringObj.Add("m_fColliderOffsetY", m_fColliderOffsetY);
		}
		dStringObj.Add("m_v3Forward", m_v3Forward);
		dStringObj.Add("m_v3Position", m_v3Position);
		dStringObj.Add("m_fViewRangeSq", m_fViewRangeSq);
		if (!SaveLoadSceneManager.bRealNull(m_focusedObject))
		{
			dStringObj.Add("m_focusedObject", SaveLoadSceneManager.iRefToID(m_focusedObject));
		}
		if (!SaveLoadSceneManager.bRealNull(m_viewConeMarker))
		{
			dStringObj.Add("m_viewConeMarker", SaveLoadSceneManager.iRefToID(m_viewConeMarker));
		}
		if (!SaveLoadSceneManager.bRealNull(m_arViewablesActive))
		{
			dStringObj.Add("m_arViewablesActive", SaveLoadSceneManager.iRefToID(m_arViewablesActive));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liObjectsInRange))
		{
			dStringObj.Add("m_liObjectsInRange", SaveLoadSceneManager.createIDCollection(m_liObjectsInRange));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liObjectsVisible))
		{
			dStringObj.Add("m_liObjectsVisible", SaveLoadSceneManager.createIDCollection(m_liObjectsVisible));
		}
		if (!SaveLoadSceneManager.bRealNull(m_trackedObjComparer))
		{
			dStringObj.Add("m_trackedObjComparer", SaveLoadSceneManager.iRefToID(m_trackedObjComparer));
		}
		dStringObj.Add("m_eAlertState", m_eAlertState);
		if (m_fDetectionRange != 0f)
		{
			dStringObj.Add("m_fDetectionRange", m_fDetectionRange);
		}
		if (!SaveLoadSceneManager.bRealNull(m_fpDetected))
		{
			dStringObj.Add("m_fpDetected", SaveLoadSceneManager.iRefToID(m_fpDetected));
		}
		if (!SaveLoadSceneManager.bRealNull(m_coroClearFpDelayed))
		{
			dStringObj.Add("m_coroClearFpDelayed", SaveLoadSceneManager.iRefToID(m_coroClearFpDelayed));
		}
		if (m_bVCFillSpeedOverride)
		{
			dStringObj.Add("m_bVCFillSpeedOverride", m_bVCFillSpeedOverride);
		}
		if (!SaveLoadSceneManager.bRealNull(m_boundaries))
		{
			dStringObj.Add("m_boundaries", SaveLoadSceneManager.iRefToID(m_boundaries));
		}
		if (!SaveLoadSceneManager.bRealNull(m_delOnAddEntry))
		{
			dStringObj.Add("m_delOnAddEntry", SaveLoadSceneManager.iRefToID(m_delOnAddEntry));
		}
		if (!SaveLoadSceneManager.bRealNull(m_delOnRemoveEntry))
		{
			dStringObj.Add("m_delOnRemoveEntry", SaveLoadSceneManager.iRefToID(m_delOnRemoveEntry));
		}
		if (!SaveLoadSceneManager.bRealNull(m_delBoundaryCheck))
		{
			dStringObj.Add("m_delBoundaryCheck", SaveLoadSceneManager.iRefToID(m_delBoundaryCheck));
		}
		dStringObj.Add("s_iUpdateInterval", s_iUpdateInterval);
		dStringObj.Add("s_iUpdateIndex", s_iUpdateIndex);
		dStringObj.Add("m_iUpdateIndex", m_iUpdateIndex);
		dStringObj.Add("m_v2DirTarget", m_v2DirTarget);
		if (!SaveLoadSceneManager.bRealNull(m_liIgnoreObjects))
		{
			dStringObj.Add("m_liIgnoreObjects", SaveLoadSceneManager.createIDCollection(m_liIgnoreObjects));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liIgnoreObjectsDynamic))
		{
			dStringObj.Add("m_liIgnoreObjectsDynamic", SaveLoadSceneManager.createIDCollection(m_liIgnoreObjectsDynamic));
		}
		if (!SaveLoadSceneManager.bRealNull(m_liStringTypesIgnored))
		{
			dStringObj.Add("m_liStringTypesIgnored", SaveLoadSceneManager.duplicateGenericListVal(m_liStringTypesIgnored, typeof(string)));
		}
		if (m_bRangeChanged)
		{
			dStringObj.Add("m_bRangeChanged", m_bRangeChanged);
		}
		dStringObj.Add("m_fDetectionRangeOnDecreaseInit", m_fDetectionRangeOnDecreaseInit);
		if (m_bDetectionRangeMaxedThisFrame)
		{
			dStringObj.Add("m_bDetectionRangeMaxedThisFrame", m_bDetectionRangeMaxedThisFrame);
		}
		if (m_bDetectionRangeMaxedLastFrame)
		{
			dStringObj.Add("m_bDetectionRangeMaxedLastFrame", m_bDetectionRangeMaxedLastFrame);
		}
		if (m_iCurrenctDetectionDisableReason != 0)
		{
			dStringObj.Add("m_iCurrenctDetectionDisableReason", m_iCurrenctDetectionDisableReason);
		}
		if (m_bTryActivateVCOnActivate)
		{
			dStringObj.Add("m_bTryActivateVCOnActivate", m_bTryActivateVCOnActivate);
		}
		if (!SaveLoadSceneManager.bRealNull(m_hidingSpotInsideOf))
		{
			dStringObj.Add("m_hidingSpotInsideOf", SaveLoadSceneManager.iRefToID(m_hidingSpotInsideOf));
		}
		base.serializeMi(ref dStringObj);
	}

	public override void deserializeMi(Dictionary<string, object> _dStringObj, Dictionary<int, UnityEngine.Object> _dIDsToObjects, Dictionary<int, UnityEngine.Object> _dIDsToAssets, Dictionary<int, object> _dIDsToClass)
	{
		if (_dStringObj.TryGetValue("m_dataSwitcher", out var value))
		{
			m_dataSwitcher = (DetectionDataSwitcher)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_dataPresets", out value))
		{
			m_dataPresets = (DetectionDataPresets)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_proximityDetection", out value))
		{
			m_proximityDetection = (ProximityDetection)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_fEyeHeight", out value))
		{
			m_fEyeHeight = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fViewAngle", out value))
		{
			m_fViewAngle = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fViewRange", out value))
		{
			m_fViewRange = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fCrawlDistance", out value))
		{
			m_fCrawlDistance = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fNoNormalVisionHeight", out value))
		{
			m_fNoNormalVisionHeight = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fNoVisionHeight", out value))
		{
			m_fNoVisionHeight = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fVisionCapBottom", out value))
		{
			m_fVisionCapBottom = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fYClampTop", out value))
		{
			m_fYClampTop = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fYClampBot", out value))
		{
			m_fYClampBot = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fVCEmptySpeed", out value))
		{
			m_fVCEmptySpeed = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fColliderHeight", out value))
		{
			m_fColliderHeight = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fColliderOffsetY", out value))
		{
			m_fColliderOffsetY = (float)value;
		}
		if (_dStringObj.TryGetValue("m_v3Forward", out value))
		{
			m_v3Forward = (Vector3)value;
		}
		if (_dStringObj.TryGetValue("m_v3Position", out value))
		{
			m_v3Position = (Vector3)value;
		}
		if (_dStringObj.TryGetValue("m_fViewRangeSq", out value))
		{
			m_fViewRangeSq = (float)value;
		}
		if (_dStringObj.TryGetValue("m_focusedObject", out value))
		{
			m_focusedObject = (TrackedObject)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_viewConeMarker", out value))
		{
			m_viewConeMarker = (TrackedObject)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_arViewablesActive", out value))
		{
			m_arViewablesActive = (Viewables.ResizeableEntryArray)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_liObjectsInRange", out value))
		{
			m_liObjectsInRange = (List<TrackedObject>)SaveLoadSceneManager.createListWithRefs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(List<TrackedObject>));
		}
		if (_dStringObj.TryGetValue("m_liObjectsVisible", out value))
		{
			m_liObjectsVisible = (List<TrackedObject>)SaveLoadSceneManager.createListWithRefs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(List<TrackedObject>));
		}
		if (_dStringObj.TryGetValue("m_trackedObjComparer", out value))
		{
			m_trackedObjComparer = (TrackedObjectsComparer)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_eAlertState", out value))
		{
			m_eAlertState = (AIHandler.AlertState)(int)value;
		}
		if (_dStringObj.TryGetValue("m_fDetectionRange", out value))
		{
			m_fDetectionRange = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fpDetected", out value))
		{
			m_fpDetected = (Footprint)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_coroClearFpDelayed", out value))
		{
			m_coroClearFpDelayed = (CoroWait)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bVCFillSpeedOverride", out value))
		{
			m_bVCFillSpeedOverride = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_boundaries", out value))
		{
			m_boundaries = (Viewables.Boundaries)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_delOnAddEntry", out value))
		{
			m_delOnAddEntry = (Viewables.delEntryOperation)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_delOnRemoveEntry", out value))
		{
			m_delOnRemoveEntry = (Viewables.delEntryOperation)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_delBoundaryCheck", out value))
		{
			m_delBoundaryCheck = (Viewables.delBoundaryCheck)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("s_iUpdateInterval", out value))
		{
			s_iUpdateInterval = (int)value;
		}
		if (_dStringObj.TryGetValue("s_iUpdateIndex", out value))
		{
			s_iUpdateIndex = (int)value;
		}
		if (_dStringObj.TryGetValue("m_iUpdateIndex", out value))
		{
			m_iUpdateIndex = (int)value;
		}
		if (_dStringObj.TryGetValue("m_v2DirTarget", out value))
		{
			m_v2DirTarget = (Vector2)value;
		}
		if (_dStringObj.TryGetValue("m_liIgnoreObjects", out value))
		{
			m_liIgnoreObjects = (List<ClickableObject>)SaveLoadSceneManager.createListWithRefs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(List<ClickableObject>));
		}
		if (_dStringObj.TryGetValue("m_liIgnoreObjectsDynamic", out value))
		{
			m_liIgnoreObjectsDynamic = (List<ClickableObject>)SaveLoadSceneManager.createListWithRefs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value, typeof(List<ClickableObject>));
		}
		if (_dStringObj.TryGetValue("m_liStringTypesIgnored", out value))
		{
			m_liStringTypesIgnored = (List<string>)SaveLoadSceneManager.duplicateGenericListVal(value, typeof(string));
		}
		if (_dStringObj.TryGetValue("m_bRangeChanged", out value))
		{
			m_bRangeChanged = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_fDetectionRangeOnDecreaseInit", out value))
		{
			m_fDetectionRangeOnDecreaseInit = (float)value;
		}
		if (_dStringObj.TryGetValue("m_bDetectionRangeMaxedThisFrame", out value))
		{
			m_bDetectionRangeMaxedThisFrame = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_bDetectionRangeMaxedLastFrame", out value))
		{
			m_bDetectionRangeMaxedLastFrame = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_iCurrenctDetectionDisableReason", out value))
		{
			m_iCurrenctDetectionDisableReason = (int)value;
		}
		if (_dStringObj.TryGetValue("m_bTryActivateVCOnActivate", out value))
		{
			m_bTryActivateVCOnActivate = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_hidingSpotInsideOf", out value))
		{
			m_hidingSpotInsideOf = (MiEditablePolyHidingSpot)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		base.deserializeMi(_dStringObj, _dIDsToObjects, _dIDsToAssets, _dIDsToClass);
	}
}
