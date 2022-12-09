; VAS = window.VAS || {};
; (function (VAS, $) {
	VAS.CreateLinesRequisition = function () {
		this.frame;
		this.windowNo;
		this.Record_ID;
		this.Table_ID;
		var $bsyDiv;
		var $self = this; //scoped self pointer
		var $OkBtn;
		var $ApplyBtn;
		var $CancelBtn;
		var $root = $('<div>');
		var SelectedRecords;
		var _ProductLookUp;
		var _ProductCtrl;
		var _RequisitionCtrl;
		var lookupRequisition = null;
		var $RequisitionControl = null;
		var prdCtrl;
		var RequisitionGrd;
		var $DivRequisitionGrid;
		var product_ID = 0;
		var requisition_ID = 0;
		var precision = 2;
		VAS.translatedTexts = null;
		var format = VIS.DisplayType.GetNumberFormat(VIS.DisplayType.Amount);
		var dotFormatter = VIS.Env.isDecimalPoint();

		this.initalize = function () {

			var elements = [
				"M_Product_ID",
				"M_Requisition_ID",
				"ASI",
				"C_UOM_ID",
				"M_RequisitionLine_ID",
			];

			VAS.translatedTexts = VIS.Msg.translate(VIS.Env.getCtx(), elements, true);

			$root.append('<div class="VIS_Pref_show vis-formouterwrpdiv">'
				+ '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="VAS_Requisition_' + $self.windowNo + '"></div></div>'
				+ '<div class= "VIS_Pref_dd"><div class="input-group vis-input-wrap" id="VAS_Product_' + $self.windowNo + '" ></div></div>'
				+ '</div>');
			$root.append('<div class="vis-crtfrm-datawrp" style="height: 48.5%;"><div id="VAS_RequisitionGrd_' + $self.windowNo + '" style="height:350px;" ></div></div>');
			$root.append('<div class="vis-ctrfrm-btnwrp">'
				+ '<input id="VAS_CancelBtn_' + $self.windowNo + '" class= "VIS_Pref_btn-2" type = "button" value = "' + VIS.Msg.getMsg("Cancel") + '">'
				+ '<input id="VAS_ApplyBtn_' + $self.windowNo + '" class="VIS_Pref_btn-2" type="button" value="' + VIS.Msg.getMsg("Apply") + '">'
				+ '<input id="VAS_OKBtn_' + $self.windowNo + '" class="VIS_Pref_btn-2" type="button" value="' + VIS.Msg.getMsg("OK") + '">'
				+ '</div>');
			createBusyIndicator();
			$bsyDiv[0].style.visibility = "visible";
		};

		// Get controls from design
		this.intialLoad = function () {
			$OkBtn = $root.find("#VAS_OKBtn_" + $self.windowNo);
			$ApplyBtn = $root.find("#VAS_ApplyBtn_" + $self.windowNo);
			$CancelBtn = $root.find("#VAS_CancelBtn_" + $self.windowNo);
			_ProductCtrl = $root.find("#VAS_Product_" + $self.windowNo);
			_RequisitionCtrl = $root.find("#VAS_Requisition_" + $self.windowNo);
			$DivRequisitionGrid = $root.find("#VAS_RequisitionGrd_" + $self.windowNo);
			LoadControls();
			InitEvents();
			$bsyDiv[0].style.visibility = "hidden";
		};

		// load dynamic vienna controls
		function LoadControls() {
			// Requisition Control
			var SqlWhere = "M_Requisition.DOcStatus IN ('CO') AND M_Requisition.IsActive='Y' AND M_Requisition.AD_Client_ID=@AD_Client_ID@"
				+ " AND M_Requisition_ID IN (SELECT Distinct M_requisition_ID FROM M_requisitionline WHERE M_RequisitionLine_ID IN"
				+ " (SELECT req.M_RequisitionLine_ID FROM M_RequisitionLine req LEFT JOIN C_RfqLine oline ON (req.M_RequisitionLine_ID = oline.M_RequisitionLine_ID)"
				+ " WHERE oline.M_RequisitionLine_ID IS NULL OR req.Qty - req.DTD001_DeliveredQty > (SELECT SUM(rl.Qty) FROM C_RfqLineQty rl INNER JOIN C_RfqLine cl ON rl.C_RfqLine_ID = cl.C_RfqLine_ID"
				+ " WHERE cl.M_RequisitionLine_ID = req.M_RequisitionLine_ID) AND oline.C_Rfq_ID IN (SELECT C_Rfq_ID FROM C_Rfq WHERE C_Rfq_ID IN (oline.C_Rfq_ID) AND DocStatus NOT IN ('RE','VO')))) ";

			lookupRequisition = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.MultiKey, "M_Requisition_ID", 0, false, SqlWhere);
			$RequisitionControl = new VIS.Controls.VTextBoxButton("M_Requisition_ID", true, false, true, VIS.DisplayType.MultiKey, lookupRequisition);

			var _RequisitionCtrlWrap = $('<div class="vis-control-wrap">');
			var _RequisitionBtnWrap = $('<div class="input-group-append">');
			_RequisitionCtrl.append(_RequisitionCtrlWrap);
			_RequisitionCtrlWrap.append($RequisitionControl.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("DTD001_Requisition") + '</label>');
			_RequisitionCtrl.append(_RequisitionBtnWrap);
			_RequisitionBtnWrap.append($RequisitionControl.getBtn(0));
			_RequisitionBtnWrap.append($RequisitionControl.getBtn(1));

			// Product Control
			_ProductLookUp = VIS.MLookupFactory.getMLookUp(VIS.Env.getCtx(), $self.windowNo, 2221, VIS.DisplayType.Search);
			prdCtrl = new VIS.Controls.VTextBoxButton("M_Product_ID", false, false, true, VIS.DisplayType.Search, _ProductLookUp);

			var _ProductCtrlWrap = $('<div class="vis-control-wrap">');
			var _ProductBtnWrap = $('<div class="input-group-append">');
			_ProductCtrl.append(_ProductCtrlWrap);
			_ProductCtrlWrap.append(prdCtrl.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VAS.translatedTexts.M_Product_ID + '</label>');
			_ProductCtrl.append(_ProductBtnWrap);
			_ProductBtnWrap.append(prdCtrl.getBtn(0));
			_ProductBtnWrap.append(prdCtrl.getBtn(1));
		};

		// Events
		function InitEvents() {
			prdCtrl.fireValueChanged = function () {
				if (prdCtrl.value != null) {
					$bsyDiv[0].style.visibility = "visible";
					product_ID = prdCtrl.value;
					RequisitionGrd.clear();
					GetRequisitionLinesData(requisition_ID, product_ID);
				}
				else {
					product_ID = 0;
					RequisitionGrd.clear();
					GetRequisitionLinesData(requisition_ID, product_ID);
				}
			};

			$RequisitionControl.fireValueChanged = function (e) {
				$bsyDiv[0].style.visibility = "visible";
				requisition_ID = $RequisitionControl.getValue();
				RequisitionGrd.clear();
				GetRequisitionLinesData(requisition_ID, product_ID);
			};

			$OkBtn.on("click touchstart", function (ev) {
				if (RequisitionGrd.records.length > 0) {
					$bsyDiv[0].style.visibility = "visible";
					GetSelectedRecords();
					CreateLines(false);
				}
				else {
					VIS.ADialog.error("VIS_PlzSelLines");
				}
			});

			$ApplyBtn.on("click touchstart", function (ev) {
				if (RequisitionGrd.records.length > 0) {
					$bsyDiv[0].style.visibility = "visible";
					GetSelectedRecords();
					CreateLines(true);
				}
				else {
					VIS.ADialog.error("VIS_PlzSelLines");
				}
			});

			$CancelBtn.on("click touchstart", function (ev) {
				$self.frame.close();
			});

		};

		// load related product grid
		this.LoadRequisitionGrid = function () {
			RequisitionGrd = null;
			RequisitionGrd = $DivRequisitionGrid.w2grid({
				name: "VAS_RequisitionGrid_" + $self.windowNo,
				recordHeight: 25,
				show: {
					toolbar: false,  // indicates if toolbar is v isible
					//columnHeaders: true,   // indicates if columns is visible
					//lineNumbers: true,  // indicates if line numbers column is visible
					selectColumn: true,  // indicates if select column is visible
					toolbarReload: false,   // indicates if toolbar reload button is visible
					toolbarColumns: true,   // indicates if toolbar columns button is visible
					toolbarSearch: false,   // indicates if toolbar search controls are visible
					toolbarAdd: false,   // indicates if toolbar add new button is visible
					toolbarDelete: true,   // indicates if toolbar delete button is visible
					toolbarSave: true,   // indicates if toolbar save button is visible
				},
				multiSelect: true,
				columns: [
					{ field: "M_Product_ID", caption: VAS.translatedTexts.M_Product_ID, sortable: false, size: '12%' },
					{ field: "C_Charge_ID", caption: VAS.translatedTexts.M_Product_ID, sortable: false, size: '12%' },
					{ field: "Product", caption: VAS.translatedTexts.M_Product_ID, sortable: false, size: '12%' },
					{ field: "C_UOM_ID", caption: VAS.translatedTexts.M_Product_ID, sortable: false, size: '12%' },
					{ field: "UOM", caption: VAS.translatedTexts.C_UOM_ID, sortable: false, size: '12%' },
					{ field: "ASI_ID", caption: VAS.translatedTexts.M_Product_ID, sortable: false, size: '12%' },
					{ field: "ReqQty", caption: VIS.Msg.getMsg("VAS_ReqQty"), sortable: false, size: '12%' },
					{ field: "PendingQty", caption: VIS.Msg.getMsg("QtyPending"), sortable: false, size: '12%' },
					{
						field: "EnteredQty", caption: VIS.Msg.getMsg("QtyEntered"), sortable: false, size: '12%', editable: { type: 'number' },
						render: function (record, index, col_index) {
							var val = record["EnteredQty"];
							val = checkcommaordot(event, val);
							return parseFloat(val).toLocaleString();
						}
					},
					{ field: "Price", caption: VIS.Msg.getMsg("Price"), sortable: false, size: '12%' },
					{ field: "M_ReqLine_ID", caption: VAS.translatedTexts.M_RequisitionLine_ID, sortable: false, size: '80px', display: false },
					{ field: "M_Requisition_ID", caption: VAS.translatedTexts.M_Requisition_ID, sortable: false, size: '80px', display: false }
				],
				records: [

				],
				onChange: function (event) {
					// Entered Qty can not be greater than Requisition Qty
					if (VIS.Utility.Util.getValueOfInt(event.value_new) > w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).ReqQty) {
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).EnteredQty = event.value_previous;
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].refreshCell(event.recid, 'EnteredQty');
						event.preventDefault();
						VIS.ADialog.error("VAS_QtyGrtrReqQty");
					}
					// Show warning on Entered Qty if greater than Pending Qty
					else if (VIS.Utility.Util.getValueOfInt(event.value_new) > w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).PendingQty) {
						//w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).EnteredQty = event.value_previous;
						//w2ui['VAS_RequisitionGrid_' + $self.windowNo].refreshCell(event.recid, 'EnteredQty');
						//event.preventDefault();
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).EnteredQty = event.value_new;
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].refreshCell(event.recid, 'EnteredQty');
						VIS.ADialog.warn("DTD001_EntQtyGrtrPendQTy");
					}
					else {
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].get(event.recid).EnteredQty = event.value_new;
						w2ui['VAS_RequisitionGrid_' + $self.windowNo].refreshCell(event.recid, 'EnteredQty');
					}
				},
				onEditField: function (event) {
					id = event.recid;
					RequisitionGrd.records[event.index][RequisitionGrd.columns[event.column].field] = checkcommaordot(event, RequisitionGrd.records[event.index][RequisitionGrd.columns[event.column].field]);
					var _value = format.GetFormatAmount(RequisitionGrd.records[event.index][RequisitionGrd.columns[event.column].field], "init", dotFormatter);
					RequisitionGrd.records[event.index][RequisitionGrd.columns[event.column].field] = format.GetConvertedString(_value, dotFormatter);
					$("#grid_VAS_RequisitionGrid_" + $self.windowNo + "_rec_" + id).keydown(function (event) {
						if (!dotFormatter && (event.keyCode == 190 || event.keyCode == 110)) {// , separator
							return false;
						}
						else if (dotFormatter && event.keyCode == 188) { // . separator
							return false;
						}
						if (event.target.value.contains(".") && (event.which == 110 || event.which == 190 || event.which == 188)) {
							if (event.target.value.indexOf('.') > -1) {
								event.target.value = event.target.value.replace('.', '');
							}
						}
						else if (event.target.value.contains(",") && (event.which == 110 || event.which == 190 || event.which == 188)) {
							if (event.target.value.indexOf(',') > -1) {
								event.target.value = event.target.value.replace(',', '');
							}
						}
						if (event.keyCode != 8 && event.keyCode != 9 && (event.keyCode < 37 || event.keyCode > 40) &&
							(event.keyCode < 48 || event.keyCode > 57) && (event.keyCode < 96 || event.keyCode > 105)
							&& event.keyCode != 109 && event.keyCode != 189 && event.keyCode != 110
							&& event.keyCode != 144 && event.keyCode != 188 && event.keyCode != 190) {
							return false;
						}
					});
				},
			});
			RequisitionGrd.hideColumn('M_Product_ID');
			RequisitionGrd.hideColumn('C_Charge_ID');
			RequisitionGrd.hideColumn('C_UOM_ID');
			RequisitionGrd.hideColumn('ASI_ID');
			RequisitionGrd.hideColumn('Price');
			RequisitionGrd.hideColumn('M_ReqLine_ID');
			RequisitionGrd.hideColumn('M_Requisition_ID');
		};

		// load Related Products of selected product
		function GetRequisitionLinesData(requisitionID, product_ID,) {
			VIS.dataContext.getJSONData(VIS.Application.contextUrl + "VCreateFrom/GetRequisitionLines", {
				"M_Requisition_ID": requisitionID, "M_Product_ID": product_ID
			}, CallbacRequisitionData);
		};

		// Callback Related Product
		function CallbacRequisitionData(data) {
			if (data != null) {
				RequisitionGrd.add(data);
			}
			$bsyDiv[0].style.visibility = "hidden";
		};

		// Get selected records from Grid
		function GetSelectedRecords() {
			SelectedRecords = [];

			var selection = RequisitionGrd.getSelection();
			for (var i = 0; i < selection.length; i++) {
				SelectedRecords.push(
					{
						M_Product_ID: RequisitionGrd.records[selection[i] - 1].M_Product_ID,
						C_Charge_ID: RequisitionGrd.records[selection[i] - 1].C_Charge_ID,
						Product: RequisitionGrd.records[selection[i] - 1].Product,
						ASI_ID: RequisitionGrd.records[selection[i] - 1].ASI_ID,
						C_UOM_ID: RequisitionGrd.records[selection[i] - 1].C_UOM_ID,
						M_ReqLine_ID: RequisitionGrd.records[selection[i] - 1].M_ReqLine_ID,
						EnteredQty: RequisitionGrd.records[selection[i] - 1].EnteredQty,
						Price: RequisitionGrd.records[selection[i] - 1].Price
					});
			}
		};

		// function to check comma or dot from given value and return new value
		function checkcommaordot(event, val) {
			var foundComma = false;
			event.value_new = VIS.Utility.Util.getValueOfString(val);
			if (event.value_new.contains(".")) {
				foundComma = true;
				var indices = [];
				for (var i = 0; i < event.value_new.length; i++) {
					if (event.value_new[i] === ".")
						indices.push(i);
				}
				if (indices.length > 1) {
					event.value_new = removeAllButLast(event.value_new, '.');
				}
			}
			if (event.value_new.contains(",")) {
				if (foundComma) {
					event.value_new = removeAllButLast(event.value_new, ',');
				}
				else {
					var indices = [];
					for (var i = 0; i < event.value_new.length; i++) {
						if (event.value_new[i] === ",")
							indices.push(i);
					}
					if (indices.length > 1) {
						event.value_new = removeAllButLast(event.value_new, ',');
					}
					else {
						event.value_new = event.value_new.replace(",", ".");
					}
				}
			}
			if (event.value_new == "") {
				event.value_new = "0";
			}
			return event.value_new;
		};

		// Remove all seperator but only bring last seperator
		function removeAllButLast(amt, seprator) {
			var parts = amt.split(seprator);
			amt = parts.slice(0, -1).join('') + '.' + parts.slice(-1);
			if (amt.indexOf('.') == (amt.length - 1)) {
				amt = amt.replace(".", "");
			}
			return amt;
		};

		// Create busy indicator
		function createBusyIndicator() {
			$bsyDiv = $("<div class='vis-apanel-busy'>");
			$bsyDiv.css({
				"position": "absolute", "width": "98%", "height": "97%", 'text-align': 'center', 'z-index': '999'
			});
			$bsyDiv[0].style.visibility = "visible";
			$root.append($bsyDiv);
		};

		// Create Related Products lines
		CreateLines = function (fromApply) {
			$.ajax({
				url: VIS.Application.contextUrl + "VCreateFrom/SaveRfqLines",
				type: "POST",
				dataType: "json",
				contentType: "application/json; charset=utf-8",
				data: JSON.stringify({ C_RFQ_ID: $self.Record_ID, Data: SelectedRecords }),

				success: function (data) {
					if (JSON.parse(data) != "") {
						prdCtrl.setValue(null, false, true);
						RequisitionGrd.clear();
						VIS.ADialog.info("", true, JSON.parse(data), "");
					}
					$bsyDiv[0].style.visibility = "hidden";
					if (!fromApply) {
						$self.frame.close();
					}
				},
				error: function () {
					$bsyDiv[0].style.visibility = "hidden";
				}
			})
		};

		this.getRoot = function () {
			return $root;
		};
	};

	VAS.CreateLinesRequisition.prototype.init = function (windowNo, frame) {
		this.frame = frame;
		this.windowNo = windowNo;
		this.Record_ID = this.frame.getRecord_ID();
		this.Table_ID = this.frame.getAD_Table_ID();
		this.initalize();
		this.intialLoad();
		this.frame.getContentGrid().append(this.getRoot());
		var ssef = this;
		window.setTimeout(function () {
			ssef.LoadRequisitionGrid();
		}, 50);
		window.setTimeout(function () {

		}, 50);
	};
	VAS.CreateLinesRequisition.prototype.setHeight = function () {
		return "575";
	};
	VAS.CreateLinesRequisition.prototype.setWidth = function () {
		return "1050";
	};

	VAS.CreateLinesRequisition.prototype.dispose = function () {
		w2ui['VAS_RequisitionGrid_' + this.windowNo].destroy();
		this.frame = null;
		this.windowNo = null;
		$bsyDiv = null;
		$self = null;
		$OkBtn = null;
		$ApplyBtn = null;
		$root = null;
		_ProductLookUp = null;
		_ProductCtrl = null;
		_RequisitionCtrl = null;
		lookupRequisition = null;
		$RequisitionControl = null;
		SelectedRecords = null;
	};
})(VAS, jQuery);
