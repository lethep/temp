using System.Collections.Generic;
using UnityEngine;

[ImplementsMiTreeLeafDelegate]
public class DetectionOrientation : MiCharacterScopeUpdate
{
	public enum TurnDirection
	{
		Right,
		Left,
		None
	}

	private const float c_fAngleEpsilon = 0.001f;

	private const float c_fMaxTurnAngle = 70f;

	public MiCharacterBoneRefs.Bone m_eRotationBone = MiCharacterBoneRefs.Bone.Neck;

	private bool m_bEnabled = true;

	private bool m_bContinuousTurning;

	private TurnDirection m_eTurnDirectionContinuous;

	private float m_fViewAngle;

	private float m_fMoveAngle;

	private float m_fMoveSpeed;

	[MiTooltip("Value must not reach 0!", MiTooltipAttribute.TooltipType.Tooltip)]
	public AnimationCurve m_acLookAroundSpeed;

	[MiTooltip("Value must not reach 0!", MiTooltipAttribute.TooltipType.Tooltip)]
	public AnimationCurve m_acContinuousTurnSpeed;

	private Animator m_animator;

	private int m_iNeckLayer;

	private Transform m_transChestBone;

	[MiDontSave]
	private bool m_bNeckReseted;

	private float m_fAngleTarget;

	private float m_fAngleEnd;

	private bool m_bLookAround;

	private bool m_bNewTurn = true;

	private TrackedObject m_objFocus;

	private bool m_bFocusSetThisFrame;

	private DetectionData m_dataFocus;

	private bool m_bKeepFocus;

	private SkillSpreader m_skillSpreaderFocus;

	private float fOrientation
	{
		get
		{
			return MiMath.fYOrientation(m_transThis);
		}
		set
		{
			m_transThis.localEulerAngles = MiMath.s_v3Up * value;
		}
	}

	public TrackedObject focus => m_objFocus;

	public void applyData(DetectionData _data)
	{
		m_bContinuousTurning = _data.m_bContinuousTurning;
		m_eTurnDirectionContinuous = _data.m_eTurnDirectionContinuous;
		m_fViewAngle = _data.fViewAngle;
		m_fMoveAngle = _data.fMoveAngle;
		m_fMoveSpeed = _data.fMoveSpeed;
		if (m_fAngleTarget != 0f)
		{
			TurnDirection value = ((!(m_fAngleTarget < 0f)) ? TurnDirection.Left : TurnDirection.Right);
			calcAngleTarget(value);
		}
	}

	protected override void MiStart()
	{
		m_dataFocus = SaveLoadSceneManager.MiAddComponent<DetectionData>(base.gameObject);
		MiUpdateHandler.registerMiUpdate(this, MiUpdateHandler.UpdateType.LateUpdate);
		m_animator = m_character.m_charVis.m_charAnimation.m_Animator;
		m_iNeckLayer = m_character.m_charVis.m_charAnimation.iLayer(AnimatorControllerLayerNames.LayerNames.Neck);
		m_transChestBone = m_character.m_charVis.m_boneRefs.tBoneRef(MiCharacterBoneRefs.Bone.Chest);
		startLookAround(_bForced: true);
	}

	public override void MiUpdate(MiUpdateHandler.UpdateType _type)
	{
		switch (_type)
		{
		case MiUpdateHandler.UpdateType.Update:
			if (m_objFocus != null)
			{
				focusObject();
			}
			else if (m_bLookAround)
			{
				lookAround();
			}
			else
			{
				lookStatic();
			}
			break;
		case MiUpdateHandler.UpdateType.LateUpdate:
			neckRotation();
			break;
		}
	}

	public override bool bDoMiUpdate(MiUpdateHandler.UpdateType _type)
	{
		return m_bEnabled && m_bMiEnabled;
	}

	protected override void MiOnDestroy()
	{
		base.MiOnDestroy();
		MiUpdateHandler.unregisterMiUpdate(this, MiUpdateHandler.UpdateType.LateUpdate);
	}

	private void neckRotation()
	{
		if (m_bLookAround && m_charNPC.bCullingGroupVisible && !MiCamHandler.bCutsceneMode)
		{
			if (m_animator.GetLayerWeight(m_iNeckLayer) != 1f)
			{
				m_animator.SetLayerWeight(m_iNeckLayer, 1f);
			}
			float value = (MiMath.fYOrientation(m_transThis) + 70f - MiMath.fYOrientation(m_transChestBone)) / 140f;
			m_animator.SetFloat(883106910, value);
			m_bNeckReseted = false;
		}
		else if (!m_bNeckReseted)
		{
			if (m_animator.GetLayerWeight(m_iNeckLayer) != 0f)
			{
				m_animator.SetLayerWeight(m_iNeckLayer, 0f);
			}
			m_animator.SetFloat(883106910, 0.5f);
			m_bNeckReseted = true;
		}
		m_bFocusSetThisFrame = false;
	}

	public void enableThis(bool _bEnable)
	{
		m_bEnabled = _bEnable;
		if (m_bEnabled)
		{
			startLookAround(0f);
			return;
		}
		endLookAround();
		_endFocus(_bInit: true);
	}

	public void startLookAround(bool _bForced = false, TurnDirection? _turnDir = null)
	{
		if ((_bForced || !m_bLookAround || m_objFocus != null || m_fAngleTarget == 0f) && m_bEnabled && !m_character.m_charInventory.bHasPersistentPossessable())
		{
			endFocus();
			m_bLookAround = true;
			m_bNewTurn = true;
			calcAngleTarget(_turnDir);
		}
	}

	private void calcAngleTarget(TurnDirection? _turnDir = null)
	{
		m_fAngleTarget = m_fMoveAngle / 2f - m_fViewAngle / 2f;
		if (_turnDir.HasValue)
		{
			m_fAngleTarget = Mathf.Abs(m_fAngleTarget) * (float)((_turnDir.Value != 0) ? 1 : (-1));
		}
		else
		{
			m_fAngleTarget *= Mathf.Sign(m_fAngleEnd) * -1f;
		}
	}

	public void startLookAround(float _fStartOrientation)
	{
		fOrientation = _fStartOrientation;
		startLookAround(_bForced: true);
	}

	public void startLookAround(TurnDirection _turnDir)
	{
		startLookAround(_bForced: false, _turnDir);
	}

	public void startLookAround(float _fStartOrientation, float _fAngle)
	{
		m_fMoveAngle = _fAngle;
		fOrientation = _fStartOrientation;
		startLookAround(_bForced: true);
	}

	private void lookAround()
	{
		float num;
		if (m_fAngleTarget == 0f)
		{
			if (m_fAngleEnd == 0f)
			{
				m_bLookAround = false;
				num = 0f;
			}
			else
			{
				float num2 = fOrientation;
				float num3 = Mathf.Abs(m_fAngleEnd);
				float time = Mathf.Abs(num2 / (2f * m_fAngleEnd));
				float num4 = -1f * Mathf.Sign(m_fAngleEnd) * m_acLookAroundSpeed.Evaluate(time) * m_fMoveSpeed * Time.deltaTime;
				num = Mathf.Clamp(num4 + num2, 0f - num3, num3);
				if (Mathf.Abs(m_fAngleEnd - num) >= Mathf.Abs(m_fAngleEnd))
				{
					m_bLookAround = false;
					num = 0f;
				}
			}
		}
		else
		{
			float num5 = fOrientation;
			float num6 = Mathf.Abs(m_fAngleTarget);
			float time2 = Mathf.Abs(m_fAngleTarget + num5) / ((float)(m_bNewTurn ? 1 : 2) * num6);
			float num7 = Mathf.Sign(m_fAngleTarget) * m_acLookAroundSpeed.Evaluate(time2) * m_fMoveSpeed * Time.deltaTime;
			num = Mathf.Clamp(num7 + num5, 0f - num6, num6);
			if (Mathf.Abs(num - m_fAngleTarget) < 0.001f)
			{
				m_fAngleTarget *= -1f;
				m_bNewTurn = false;
			}
		}
		fOrientation = num;
	}

	public void endLookAround()
	{
		m_fAngleTarget = 0f;
		m_fAngleEnd = fOrientation;
	}

	public MiTreeLeaf.Result _startLookAround(bool _bInit)
	{
		if (_bInit)
		{
			startLookAround();
		}
		return MiTreeLeaf.Result.Success;
	}

	public MiTreeLeaf.Result _endLookAround(bool _bInit)
	{
		if (_bInit)
		{
			endLookAround();
		}
		return MiTreeLeaf.Result.Success;
	}

	private void lookStatic()
	{
		fOrientation = Mathf.Sin(MiTime.time * MiSingletonSaveMortal<AISettingsGlobal>.instance.m_fViewConeStaticSpeed) * MiSingletonSaveMortal<AISettingsGlobal>.instance.m_fViewConeStaticAngle;
	}

	private void focusObject()
	{
		if ((m_objFocus.state < Detection.DetectionState.Visible && !m_bKeepFocus) || m_objFocus.transViewable == null)
		{
			endFocus();
			return;
		}
		if (m_bKeepFocus && m_objFocus.eType != TrackedObject.Type.Player)
		{
			Vector3 from = m_character.m_charVelocity.v3Velocity;
			if (from.sqrMagnitude <= 0.1f)
			{
				from = m_character.trans.forward;
			}
			if (Mathf.Abs(Vector3.Angle(from, m_objFocus.transViewable.position - m_character.trans.position)) > MiSingletonSaveMortal<AISettingsGlobal>.instance.m_fBreakFocusAngle)
			{
				endFocus();
				startLookAround();
				return;
			}
		}
		if (MiSingletonSaveMortal<MiPlayerInput>.instance.m_freezeState.freezeStyle != FreezeState.FreezeStyle.SlowMoe || m_objFocus.eType != TrackedObject.Type.Player || m_bKeepFocus)
		{
			m_character.m_charOrientation.lookAtYRotOnly(m_objFocus.transViewable);
			fOrientation = 0f;
		}
		else if (m_bLookAround)
		{
			lookAround();
		}
	}

	public MiTreeLeaf.Result _bFreezeModeIsSlowMo(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(MiSingletonSaveMortal<MiPlayerInput>.instance.m_freezeState.freezeStyle == FreezeState.FreezeStyle.SlowMoe);
	}

	public void resetKeepFocus()
	{
		m_bKeepFocus = false;
	}

	private bool setFocusObject(TrackedObject.Type _objType)
	{
		if (m_bFocusSetThisFrame)
		{
			return false;
		}
		TrackedObject nearestVisibleObjOfType = m_charNPC.m_detection.getNearestVisibleObjOfType(_objType);
		if (nearestVisibleObjOfType != null)
		{
			endFocus();
			m_bFocusSetThisFrame = true;
			m_objFocus = nearestVisibleObjOfType;
			if (MiSingletonSaveMortal<MiPlayerInput>.instance.m_freezeState.freezeStyle != FreezeState.FreezeStyle.SlowMoe || _objType != TrackedObject.Type.Player)
			{
				m_character.m_miMovement.stop();
			}
			m_objFocus.viewable.focus(m_charNPC);
			setVCFocusData();
			SkillSpreader activeSkillSpreader = m_charNPC.m_ai.getActiveSkillSpreader();
			if (activeSkillSpreader != null)
			{
				MiCharacterTargetLookAt target = new MiCharacterTargetLookAt(m_objFocus.transViewable.position, 0f);
				m_skillSpreaderFocus = m_charNPC.m_ai.trySpreadSkill(Skill.SkillType.Wait, target, SkillSpreader.SpreadType.WaitAll, _bRegisterFocus: true);
			}
			return true;
		}
		return false;
	}

	private void setVCFocusData()
	{
		m_dataFocus.copyValues(m_charNPC.m_detection.m_dataSwitcher.getCurrent());
		AISettingsGlobal instance = MiSingletonSaveMortal<AISettingsGlobal>.instance;
		m_dataFocus.setViewAngle(instance.m_fViewAngleFocus);
		m_dataFocus.setMoveAngle(instance.m_fMoveAngleFocus);
		m_dataFocus.setMoveSpeed(instance.m_fMoveSpeedFocus);
		m_charNPC.m_detection.m_dataSwitcher.add(m_dataFocus, null, instance.m_fToFocusDuration, instance.m_acToFocusData);
	}

	private bool removeVCFocusData(MiCharacterNPC _npc)
	{
		return m_charNPC.m_detection.m_dataSwitcher.remove(m_dataFocus);
	}

	public MiTreeLeaf.Result _focusWhileMoving(bool _bInit)
	{
		if (m_objFocus != null)
		{
			m_character.m_miMovement.resume();
			m_bKeepFocus = true;
			return MiTreeLeaf.Result.Success;
		}
		return MiTreeLeaf.Result.Failure;
	}

	public MiTreeLeaf.Result _focusPlayer(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.Player));
	}

	public MiTreeLeaf.Result _focusEnemy(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.Enemy));
	}

	public MiTreeLeaf.Result _focusNPC(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.NPC));
	}

	public MiTreeLeaf.Result _focusDistraction(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.Distraction));
	}

	public MiTreeLeaf.Result _focusFootprint(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.Footprint));
	}

	public MiTreeLeaf.Result _focusLightSource(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(setFocusObject(TrackedObject.Type.LightSource));
	}

	public void endFocus(TrackedObject _obj)
	{
		if (m_objFocus != null && m_objFocus == _obj)
		{
			endFocus();
		}
	}

	public void endFocus()
	{
		if (m_skillSpreaderFocus != null)
		{
			if (m_bKeepFocus)
			{
				m_skillSpreaderFocus.clearNPCFocus();
			}
			else
			{
				m_skillSpreaderFocus.registerEndFocus(m_charNPC);
			}
			m_skillSpreaderFocus = null;
		}
		if (m_objFocus != null)
		{
			m_objFocus.viewable.endFocus(m_charNPC);
			m_charNPC.m_detection.m_dataSwitcher.remove(m_dataFocus, null, MiSingletonSaveMortal<AISettingsGlobal>.instance.m_fEndFocusDuration, MiSingletonSaveMortal<AISettingsGlobal>.instance.m_acEndFocusData);
		}
		m_objFocus = null;
		m_character.m_miMovement.resume();
		m_bKeepFocus = false;
	}

	public MiTreeLeaf.Result _endFocus(bool _bInit)
	{
		if (_bInit)
		{
			endFocus();
		}
		return MiTreeLeaf.Result.Success;
	}

	public bool bHasFocusType(TrackedObject.Type _eType)
	{
		if (m_objFocus != null && m_objFocus.objRef == null)
		{
			m_objFocus = null;
		}
		if (m_objFocus != null && m_objFocus.eType == _eType)
		{
			m_bFocusSetThisFrame = true;
			return true;
		}
		return false;
	}

	public MiTreeLeaf.Result _bHasFocus(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_objFocus != null);
	}

	public MiTreeLeaf.Result _bKeepFocus(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(m_bKeepFocus);
	}

	public MiTreeLeaf.Result _bHasFocusPlayer(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.Player));
	}

	public MiTreeLeaf.Result _bHasFocusEnemy(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.Enemy));
	}

	public MiTreeLeaf.Result _bHasFocusNPC(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.NPC));
	}

	public MiTreeLeaf.Result _bHasFocusDistraction(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.Distraction));
	}

	public MiTreeLeaf.Result _bHasFocusFootprint(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.Footprint));
	}

	public MiTreeLeaf.Result _bHasFocusLightSource(bool _bInit)
	{
		return MiTreeLeaf.boolToResult(bHasFocusType(TrackedObject.Type.LightSource));
	}

	public override void serializeMi(ref Dictionary<string, object> dStringObj)
	{
		dStringObj.Add("m_eRotationBone", m_eRotationBone);
		if (!m_bEnabled)
		{
			dStringObj.Add("m_bEnabled", m_bEnabled);
		}
		if (m_bContinuousTurning)
		{
			dStringObj.Add("m_bContinuousTurning", m_bContinuousTurning);
		}
		dStringObj.Add("m_eTurnDirectionContinuous", m_eTurnDirectionContinuous);
		dStringObj.Add("m_fViewAngle", m_fViewAngle);
		dStringObj.Add("m_fMoveAngle", m_fMoveAngle);
		dStringObj.Add("m_fMoveSpeed", m_fMoveSpeed);
		if (!SaveLoadSceneManager.bRealNull(m_acLookAroundSpeed))
		{
			dStringObj.Add("m_acLookAroundSpeed", SaveLoadSceneManager.iRefToID(m_acLookAroundSpeed));
		}
		if (!SaveLoadSceneManager.bRealNull(m_acContinuousTurnSpeed))
		{
			dStringObj.Add("m_acContinuousTurnSpeed", SaveLoadSceneManager.iRefToID(m_acContinuousTurnSpeed));
		}
		if (!SaveLoadSceneManager.bRealNull(m_animator))
		{
			dStringObj.Add("m_animator", SaveLoadSceneManager.iRefToID(m_animator));
		}
		dStringObj.Add("m_iNeckLayer", m_iNeckLayer);
		if (!SaveLoadSceneManager.bRealNull(m_transChestBone))
		{
			dStringObj.Add("m_transChestBone", SaveLoadSceneManager.iRefToID(m_transChestBone));
		}
		dStringObj.Add("m_fAngleTarget", m_fAngleTarget);
		dStringObj.Add("m_fAngleEnd", m_fAngleEnd);
		if (m_bLookAround)
		{
			dStringObj.Add("m_bLookAround", m_bLookAround);
		}
		if (!m_bNewTurn)
		{
			dStringObj.Add("m_bNewTurn", m_bNewTurn);
		}
		if (!SaveLoadSceneManager.bRealNull(m_objFocus))
		{
			dStringObj.Add("m_objFocus", SaveLoadSceneManager.iRefToID(m_objFocus));
		}
		if (m_bFocusSetThisFrame)
		{
			dStringObj.Add("m_bFocusSetThisFrame", m_bFocusSetThisFrame);
		}
		if (!SaveLoadSceneManager.bRealNull(m_dataFocus))
		{
			dStringObj.Add("m_dataFocus", SaveLoadSceneManager.iRefToID(m_dataFocus));
		}
		if (m_bKeepFocus)
		{
			dStringObj.Add("m_bKeepFocus", m_bKeepFocus);
		}
		if (!SaveLoadSceneManager.bRealNull(m_skillSpreaderFocus))
		{
			dStringObj.Add("m_skillSpreaderFocus", SaveLoadSceneManager.iRefToID(m_skillSpreaderFocus));
		}
		base.serializeMi(ref dStringObj);
	}

	public override void deserializeMi(Dictionary<string, object> _dStringObj, Dictionary<int, Object> _dIDsToObjects, Dictionary<int, Object> _dIDsToAssets, Dictionary<int, object> _dIDsToClass)
	{
		if (_dStringObj.TryGetValue("m_eRotationBone", out var value))
		{
			m_eRotationBone = (MiCharacterBoneRefs.Bone)(int)value;
		}
		if (_dStringObj.TryGetValue("m_bEnabled", out value))
		{
			m_bEnabled = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_bContinuousTurning", out value))
		{
			m_bContinuousTurning = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_eTurnDirectionContinuous", out value))
		{
			m_eTurnDirectionContinuous = (TurnDirection)(int)value;
		}
		if (_dStringObj.TryGetValue("m_fViewAngle", out value))
		{
			m_fViewAngle = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fMoveAngle", out value))
		{
			m_fMoveAngle = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fMoveSpeed", out value))
		{
			m_fMoveSpeed = (float)value;
		}
		if (_dStringObj.TryGetValue("m_acLookAroundSpeed", out value))
		{
			m_acLookAroundSpeed = (AnimationCurve)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_acContinuousTurnSpeed", out value))
		{
			m_acContinuousTurnSpeed = (AnimationCurve)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_animator", out value))
		{
			m_animator = (Animator)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_iNeckLayer", out value))
		{
			m_iNeckLayer = (int)value;
		}
		if (_dStringObj.TryGetValue("m_transChestBone", out value))
		{
			m_transChestBone = (Transform)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_fAngleTarget", out value))
		{
			m_fAngleTarget = (float)value;
		}
		if (_dStringObj.TryGetValue("m_fAngleEnd", out value))
		{
			m_fAngleEnd = (float)value;
		}
		if (_dStringObj.TryGetValue("m_bLookAround", out value))
		{
			m_bLookAround = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_bNewTurn", out value))
		{
			m_bNewTurn = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_objFocus", out value))
		{
			m_objFocus = (TrackedObject)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bFocusSetThisFrame", out value))
		{
			m_bFocusSetThisFrame = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_dataFocus", out value))
		{
			m_dataFocus = (DetectionData)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		if (_dStringObj.TryGetValue("m_bKeepFocus", out value))
		{
			m_bKeepFocus = (bool)value;
		}
		if (_dStringObj.TryGetValue("m_skillSpreaderFocus", out value))
		{
			m_skillSpreaderFocus = (SkillSpreader)SaveLoadSceneManager.recreateRefsFromIDs(_dIDsToObjects, _dIDsToAssets, _dIDsToClass, value);
		}
		base.deserializeMi(_dStringObj, _dIDsToObjects, _dIDsToAssets, _dIDsToClass);
	}
}
