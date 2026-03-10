import React, { useEffect, useState } from "react";

const ProductUOMComponent = ({ windowNo, frame }) => {

    /* ---------- VIS Context ---------- */
    const ctx = VIS.Env.getCtx();
    const C_UOM_ID = ctx.getWindowContext(windowNo, "C_UOM_ID");
    const M_Product_ID = ctx.getWindowContext(windowNo, "M_Product_ID");

    /* ---------- VIS Combo Instances ---------- */
    let cmbUOM, cmbPU, cmbSU, cmbCU;

    /* ---------- State ---------- */
    const [disablePU, setDisablePU] = useState(true);
    const [disableSU, setDisableSU] = useState(true);
    const [disableCU, setDisableCU] = useState(true);
    const [busyDiv, setBusyDiv] = useState(false);

    const [puSUQty, setPuSUQty] = useState("1");
    const [puPUQty, setPuPUQty] = useState("1");
    const [suSUQty, setSuSUQty] = useState("1");
    const [suPUQty, setSuPUQty] = useState("1");
    const [cuSUQty, setCuSUQty] = useState("1");
    const [cuPUQty, setCuPUQty] = useState("1");


    /* ---------- Helpers ---------- */
    const invertRate = (val, setter) => {
        if (!val || Number(val) === 0) setter("0");
        else setter((1 / Number(val)).toFixed(12));
    };

    const allowOnlyNumber = (e) => {
        if (!/[0-9]/.test(e.key)) e.preventDefault();
    };

    /* ---------- Init ---------- */
    useEffect(() => {

        setBusyDiv(true);
        const lookup = VIS.MLookupFactory.get(
            ctx,
            windowNo,
            0,
            VIS.DisplayType.TableDir,
            "C_UOM_ID",
            0,
            false
        );

        const createCombo = (mandatory) =>
            new VIS.Controls.VComboBox(
                "C_UOM_ID",
                mandatory,
                false,
                true,
                lookup,
                150,
                VIS.DisplayType.TableDir
            );

        /* ---------- Base UOM ---------- */
        cmbUOM = createCombo(true);
        cmbUOM.setValue(C_UOM_ID);
        document
            .getElementById(`VAS_UOM_${windowNo}`)
            .appendChild(cmbUOM.getControl()[0]);

        /* ---------- Purchase UOM ---------- */
        cmbPU = createCombo(false);
        cmbPU.setValue(C_UOM_ID);
        document
            .getElementById(`VAS_PU_${windowNo}`)
            .appendChild(cmbPU.getControl()[0]);

        cmbPU.fireValueChanged = () => {
            const same = cmbPU.getValue() === C_UOM_ID;
            setDisablePU(same);
            setPuSUQty(same ? "1" : "");
            setPuPUQty(same ? "1" : "");
        };

        /* ---------- Sales UOM ---------- */
        cmbSU = createCombo(false);
        cmbSU.setValue(C_UOM_ID);
        document
            .getElementById(`VAS_SU_${windowNo}`)
            .appendChild(cmbSU.getControl()[0]);

        cmbSU.fireValueChanged = () => {
            const same = cmbSU.getValue() === C_UOM_ID;
            setDisableSU(same);
            setSuSUQty(same ? "1" : "");
            setSuPUQty(same ? "1" : "");
        };

        /* ---------- Consumable UOM ---------- */
        cmbCU = createCombo(false);
        cmbCU.setValue(C_UOM_ID);
        document
            .getElementById(`VAS_CU_${windowNo}`)
            .appendChild(cmbCU.getControl()[0]);

        cmbCU.fireValueChanged = () => {
            const same = cmbCU.getValue() === C_UOM_ID;
            setDisableCU(same);
            setCuSUQty(same ? "1" : "");
            setCuPUQty(same ? "1" : "");
        };

        setBusyDiv(false);

        return () => {
            cmbUOM?.dispose?.();
            cmbPU?.dispose?.();
            cmbSU?.dispose?.();
            cmbCU?.dispose?.();
        };

    }, [windowNo, C_UOM_ID]);

    /* ---------- Save ---------- */
    const saveTask = () => {

        setBusyDiv(true);

        if (!cmbPU?.getValue()) {
            setBusyDiv(false);
            return VIS.ADialog.info("VAS_PuUnitMandatory");
        }

        if (!cmbSU?.getValue()) {
            setBusyDiv(false);
            return VIS.ADialog.info("VAS_SuUnitMandatory");
        }

        if (!cmbCU?.getValue()) {
            setBusyDiv(false);
            return VIS.ADialog.info("VAS_CuUnitMandatory");
        }

        const invalid =
            [puSUQty, puPUQty, suSUQty, suPUQty, cuSUQty, cuPUQty]
                .some(v => !v || v === "0");

        if (invalid) {
            setBusyDiv(false);
            return VIS.ADialog.info("ProductUOMConversionRateError");
        }

        // 👉 keep your existing multiplyRateList + fetch logic here

        setBusyDiv(false);
    };

    /* ---------- UI ---------- */
    return (
        <div>
            {/* Busy */}
            {busyDiv && (<div
                id={`VAS_Busy_${windowNo}`}
                className="vis-busyindicatorouterwrap">
                <div className="vis-busyindicatorinnerwrap">
                    <i className="vis-busyindicatordiv" />
                </div>
            </div>)}
            <div className="VAS-flyout-body">
                <div id={`VAS_UOM_${windowNo}`} />
                <RateBlock
                    id={`VAS_PU_${windowNo}`}
                    suValue={puSUQty}
                    puValue={puPUQty}
                    disabled={disablePU}
                    onSUChange={v => { setPuSUQty(v); invertRate(v, setPuPUQty); }}
                    onPUChange={v => { setPuPUQty(v); invertRate(v, setPuSUQty); }}
                    onKeyPress={allowOnlyNumber}
                />

                <RateBlock
                    id={`VAS_SU_${windowNo}`}
                    suValue={suSUQty}
                    puValue={suPUQty}
                    disabled={disableSU}
                    onSUChange={v => { setSuSUQty(v); invertRate(v, setSuPUQty); }}
                    onPUChange={v => { setSuPUQty(v); invertRate(v, setSuSUQty); }}
                    onKeyPress={allowOnlyNumber}
                />

                <RateBlock
                    id={`VAS_CU_${windowNo}`}
                    suValue={cuSUQty}
                    puValue={cuPUQty}
                    disabled={disableCU}
                    onSUChange={v => { setCuSUQty(v); invertRate(v, setCuPUQty); }}
                    onPUChange={v => { setCuPUQty(v); invertRate(v, setCuSUQty); }}
                    onKeyPress={allowOnlyNumber}
                />

                <div className="text-right">
                    <button onClick={() => frame.close()}>
                        {VIS.Msg.getMsg("VAS_Cancel")}
                    </button>
                    <button onClick={saveTask}>
                        {VIS.Msg.getMsg("VAS_Save")}
                    </button>
                </div>
            </div>
        </div >
    );
};

/* ---------- Rate Block ---------- */
const RateBlock = ({
    id,
    suValue,
    puValue,
    disabled,
    onSUChange,
    onPUChange,
    onKeyPress
}) => (
    <div className="VIS_Pref_show">
        <div id={id} className="input-group vis-input-wrap" />

        <input
            value={suValue}
            disabled={disabled}
            onKeyPress={onKeyPress}
            onChange={e => onSUChange(e.target.value)}
        />

        <input
            value={puValue}
            disabled={disabled}
            onKeyPress={onKeyPress}
            onChange={e => onPUChange(e.target.value)}
        />
    </div>
);

export default ProductUOMComponent;
