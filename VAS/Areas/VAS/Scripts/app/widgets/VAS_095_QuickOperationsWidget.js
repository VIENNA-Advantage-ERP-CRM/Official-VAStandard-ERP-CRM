/**
 * Overall Inventory - Quick Operations launcher.
 *
 * Summary Message Table
 *  # | Current Text                              | Message Key
 * ---+-------------------------------------------+---------------------------------------
 *  1 | Quick Operations                          | VAS_095_QuickOperations
 *  2 | Stock Management Form                     | VAS_095_StockManagementForm
 *  3 | Add, move, or block stock                 | VAS_095_AddMoveOrBlockStock
 *  4 | Material Issue Form                       | VAS_095_MaterialIssueForm
 *  5 | Issue stock to work order or cost centre  | VAS_095_IssueStockToWorkOrder
 *  6 | Unable to open the form.                  | VAS_095_CouldntOpenForm
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_095_QuickOperationsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-quick-operations-root">');
        var $stockButton;
        var $issueButton;
        var request;
        var stockManagementFormId = 0;
        var materialIssueFormId = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function showOpenError() {
            VIS.ADialog.error(
                'VAS_095_CouldntOpenForm',
                true,
                '',
                label('VAS_095_CouldntOpenForm', 'Unable to open the form.')
            );
        }

        function openStockManagementForm() {
            if (stockManagementFormId <= 0) {
                showOpenError();
                return;
            }
            VIS.viewManager.startForm(stockManagementFormId);
        }

        function openMaterialIssueForm() {
            if (materialIssueFormId <= 0) {
                showOpenError();
                return;
            }
            VIS.viewManager.startForm(materialIssueFormId);
        }

        function setLoading(loading) {
            if ($stockButton) { $stockButton.prop('disabled', loading); }
            if ($issueButton) { $issueButton.prop('disabled', loading); }
        }

        function parseResponse(response) {
            var result = response;
            if (typeof result === 'string' && result) { result = JSON.parse(result); }
            if (typeof result === 'string' && result) { result = JSON.parse(result); }
            return result;
        }

        function loadFormIds() {
            if (request && request.readyState !== 4) { request.abort(); }

            stockManagementFormId = 0;
            materialIssueFormId = 0;
            setLoading(true);

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_095_QuickOperationsWidget/GetFormIds',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result;
                    try {
                        result = parseResponse(response);
                    }
                    catch (ignore) {
                        result = null;
                    }

                    if (!result || result.error) { return; }
                    stockManagementFormId = Number(result.stock_management_form_id || 0);
                    materialIssueFormId = Number(result.material_issue_form_id || 0);
                },
                complete: function () {
                    setLoading(false);
                }
            });
        }

        this.Initalize = function () {
            var stockTitle = label('VAS_095_StockManagementForm', 'Stock Management Form');
            var stockDescription = label('VAS_095_AddMoveOrBlockStock', 'Add, move, or block stock');
            var issueTitle = label('VAS_095_MaterialIssueForm', 'Material Issue Form');
            var issueDescription = label('VAS_095_IssueStockToWorkOrder', 'Issue stock to work order or cost centre');
            var $card = $(
                '<div class="MPC-quick-operations-card">' +
                    '<div class="MPC-quick-operations-header">' +
                        '<span class="MPC-quick-operations-header-icon" aria-hidden="true">' +
                            '<svg class="MPC-quick-operations-header-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>' +
                            '</svg>' +
                        '</span>' +
                        '<span class="MPC-quick-operations-title"></span>' +
                    '</div>' +
                    '<div class="MPC-quick-operations-actions">' +
                        '<button type="button" class="MPC-quick-operations-action MPC-quick-operations-stock">' +
                            '<span class="MPC-quick-operations-action-icon" aria-hidden="true">' +
                                '<svg class="MPC-quick-operations-action-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                    '<path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>' +
                                    '<polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>' +
                                    '<line x1="12" y1="22.08" x2="12" y2="12"></line>' +
                                '</svg>' +
                            '</span>' +
                            '<span class="MPC-quick-operations-action-title MPC-quick-operations-stock-title"></span>' +
                            '<span class="MPC-quick-operations-action-description MPC-quick-operations-stock-description"></span>' +
                        '</button>' +
                        '<button type="button" class="MPC-quick-operations-action MPC-quick-operations-issue">' +
                            '<span class="MPC-quick-operations-action-icon" aria-hidden="true">' +
                                '<svg class="MPC-quick-operations-action-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                    '<line x1="22" y1="2" x2="11" y2="13"></line>' +
                                    '<polygon points="22 2 15 22 11 13 2 9 22 2"></polygon>' +
                                '</svg>' +
                            '</span>' +
                            '<span class="MPC-quick-operations-action-title MPC-quick-operations-issue-title"></span>' +
                            '<span class="MPC-quick-operations-action-description MPC-quick-operations-issue-description"></span>' +
                        '</button>' +
                    '</div>' +
                '</div>'
            );

            $card.find('.MPC-quick-operations-title').text(label('VAS_095_QuickOperations', 'Quick Operations'));
            $card.find('.MPC-quick-operations-stock-title').text(stockTitle);
            $card.find('.MPC-quick-operations-stock-description').text(stockDescription);
            $card.find('.MPC-quick-operations-issue-title').text(issueTitle);
            $card.find('.MPC-quick-operations-issue-description').text(issueDescription);

            $stockButton = $card.find('.MPC-quick-operations-stock')
                .attr('aria-label', stockTitle + '. ' + stockDescription)
                .on('click.MPCQuickOperations', openStockManagementForm);

            $issueButton = $card.find('.MPC-quick-operations-issue')
                .attr('aria-label', issueTitle + '. ' + issueDescription)
                .on('click.MPCQuickOperations', openMaterialIssueForm);

            $root.append($card);
            loadFormIds();
        };

        this.refreshWidget = function () {
            loadFormIds();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            if ($stockButton) { $stockButton.off('.MPCQuickOperations'); }
            if ($issueButton) { $issueButton.off('.MPCQuickOperations'); }
            $root.remove();
        };
    };

    VAS.VAS_095_QuickOperationsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_095_QuickOperationsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_095_QuickOperationsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_095_QuickOperationsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
