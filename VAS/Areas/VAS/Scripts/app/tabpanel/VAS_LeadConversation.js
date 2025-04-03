; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_LeadConversation = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        var $root;
        var $self = this;
        var $ResTxt;
        var $InputTxt;
        var $Btn;
        var apiKey = "";
        var api_Url = "https://api.openai.com/v1/chat/completions";
        var asst_ID = "asst_YzDPtqW1Fa0CX3ZAabFXdJTy";
        var newThread = false;
        var runProcess = false;
        var thread_id = "";
        var runStatus = false;
        var statusType = "";
        var oppData = [];
        var PromptMsg = "";
        var gotRespStatus = "";
        var imgUrl = "";

        this.init = function () {
            $root = $('<div class="VAS-chatboat-container" style="right: 0; bottom: 0;">' +
                '<div class="VAS-chatbaot-header" > <div class="VAS-aura-img"> <div class="VAS-thumbImg"><img src="Areas/VAS/Content/Images/AI.png" alt=""></div> Ask AURA</div> <a href="#"></a> </div>' +
                '<div id="VAS_ResponseTxt' + $self.windowNo + '" class="VAS-chat-body">' +
                ' </div>' +
                ' <div class="VAS-chatTextarea-col">' +
                ' <textarea id="VAS_InpTxt' + $self.windowNo + '" name="" id="" placeholder="Start Conversation"></textarea>' +
                ' <button id="VAS_EntrBtn' + $self.windowNo + '" class="VAS-sendBtn"><i class="fa fa-paper-plane" aria-hidden="true"></i></button>' +
                ' </div>' +
                '</div>');
            getControl();
            events();
            busyIndicator();
        };

        function getControl() {
            $ResTxt = $root.find("#VAS_ResponseTxt" + $self.windowNo);
            $InputTxt = $root.find("#VAS_InpTxt" + $self.windowNo);
            $Btn = $root.find("#VAS_EntrBtn" + $self.windowNo);
        };

        function busyIndicator() {
            $BusyIndicator = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
            $BusyIndicator[0].style.visibility = "hidden";
            $root.append($BusyIndicator);
        };

        function SetBusy(value) {
            if (value) {
                $BusyIndicator[0].style.visibility = "visible";
            }
            else {
                $BusyIndicator[0].style.visibility = "hidden";
            }
        }

        function events() {
            $Btn.on("click", function () {
                SetBusy(true);
                var txt = $InputTxt.val();
                $InputTxt.val("");
                if (txt != '' || txt != null) {
                    if (txt != '' || txt != null) {
                        $ResTxt.append(' <div class= "VAS-userChat-comment VAS-row-reverse" > ' +
                            '  <div class="VAS-thumbImg"><img src="' + VIS.Application.contextFullUrl + imgUrl + '" alt=""></div>' +
                            ' <div class="VAS-chat-comments-col ">' +
                            '   <div class="VAS-chatuser-name VAS-row-reverse">' + VIS.context.getAD_User_Name() + '<span>12:00PM</span></div>' +
                            '  <div class="VAS-chatBox VAS-chat-radius">' + txt + '</div>' +
                            '</div>' +
                            ' </div>');
                        scrollToBottom();

                    };
                    AddMessage(txt);
                } else {
                    SetBusy(false);
                }

            });
        }

        function CreateThread() {
            $ResTxt.empty();
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads', // OpenAI's endpoint
                    //async: false,
                    type: 'POST',
                    contentType: 'application/json',
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },

                    success: function (response) {
                        thread_id = response.id;
                        newThread = true;
                        AddMessage(PromptMsg);
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });
            }
            catch (error) {
                alert(error);
                SetBusy(false);
            }
        };

        function AddMessage(msg) {
            runProcess = false;
            runStatus = false;
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/messages', // OpenAI's endpoint
                    type: 'POST',
                    contentType: 'application/json',
                    //async: false,
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },
                    data: JSON.stringify({
                        "role": "user",
                        "content": msg
                    }),
                    success: function (response) {
                        if (response.id != null) {
                            RunMessage();
                        };
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });

            }
            catch (error) {
                alert(error);
                SetBusy(false);
            }
        };

        function RunMessage() {
            statusType = "";
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/runs', // OpenAI's endpoint
                    type: 'POST',
                    contentType: 'application/json',
                    //async: false,
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },
                    data: JSON.stringify({
                        "assistant_id": asst_ID
                    }),
                    success: function (response) {
                        if (response != null) {
                            while (!runStatus) {
                                CheckRunStatus(response.id);
                            }
                            if (statusType == "completed") {
                                GetResponseMessage(response.id);
                            }
                            else if (statusType == "failed") {
                                SetBusy(false);
                            }
                        }
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });

            }
            catch (error) {
                alert(error);
                SetBusy(false);
            }
        };

        function CheckRunStatus(run_ID) {
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/runs/' + run_ID, // OpenAI's endpoint
                    type: 'GET',
                    contentType: 'application/json',
                    async: false,
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },

                    success: function (response) {
                        if (response != null) {
                            if (response.status == "failed" || response.status == "completed" || response.status == "requires_action") {
                                if (response.status == "completed") {
                                    runStatus = true;
                                }
                                else if (response.status == "failed") {
                                    appendMessageOutput("failed Message");
                                    runStatus = true;
                                }
                                statusType = response.status;
                                if (statusType == "requires_action" && !runProcess) {
                                    if (response.required_action.submit_tool_outputs.tool_calls.length > 0 &&
                                        response.required_action.submit_tool_outputs.tool_calls[0].type == "function") {

                                        runProcess = true;
                                        if (response.required_action.submit_tool_outputs.tool_calls[0].function.name == "create_opportunitylines") {
                                            GenerateLines(response.required_action.submit_tool_outputs.tool_calls);
                                        }
                                        else if (response.required_action.submit_tool_outputs.tool_calls[0].function.name == "lead_prospect") {
                                            ConvertToProspect(response.required_action.submit_tool_outputs.tool_calls);
                                        }
                                        else {
                                            ConvertToOpportunity(response.required_action.submit_tool_outputs.tool_calls);
                                        }
                                        SubmitToolResponse(run_ID, response.required_action.submit_tool_outputs.tool_calls[0].id);
                                    }
                                }
                            }
                        }
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });
            }
            catch (error) {
                alert(error);
                SetBusy(false);
            }
        };

        function prepareData(data) {
            data = JSON.parse(data[0].function.arguments);
            if (data != null && data.products != null && data.products.length > 0) {
                for (var i = 0; i < data.products.length; i++) {
                    var pdata = {};
                    pdata["M_Product_ID"] = data.products[i].m_product_id;
                    pdata["C_UOM_ID"] = data.products[i].c_uom_id;
                    pdata["Quantity"] = data.products[i].mandays;
                    pdata["Price"] = data.products[i].price;
                    oppData.push(pdata);
                }
            }
        }

        function GenerateLines(dataRes) {
            if (dataRes.length > 0) {
                oppData = [];
                prepareData(dataRes);
                try {
                    $.ajax({
                        url: VIS.Application.contextUrl + "VAS_Lead/GenerateLines",
                        type: "POST",
                        datatype: "json",
                        contentType: "application/json; charset=utf-8",
                        data: JSON.stringify({ Record_ID: $self.record_ID, ProductData: oppData }),
                        async: false,
                        success: function (data) {
                            gotRespStatus = JSON.parse(data);
                        },
                        error: function (err) {
                            gotRespStatus = err;
                            SetBusy(false);
                            return false;
                        }
                    });
                }
                catch (error) {
                    alert(error);
                    SetBusy(false);
                }
            }
        };

        function ConvertToProspect(dataRes) {
            if (dataRes.length > 0) {
                postData = [];
                //prepareData(dataRes);
                try {
                    $.ajax({
                        url: VIS.Application.contextUrl + "VAS_Lead/ConvertProspect",
                        type: "POST",
                        datatype: "json",
                        contentType: "application/json; charset=utf-8",
                        data: JSON.stringify({ Record_ID: $self.record_ID }),
                        async: false,
                        success: function (data) {
                            gotRespStatus = JSON.parse(data);
                        },
                        error: function () {
                            SetBusy(false);
                        }
                    });
                }
                catch (error) {
                    alert(error);
                    SetBusy(false);
                }
            }
        };

        function ConvertToOpportunity(dataRes) {
            if (dataRes.length > 0) {
                postData = [];
                //prepareData(dataRes);
                try {
                    $.ajax({
                        url: VIS.Application.contextUrl + "VAS_Lead/GenerateOpprtunity",
                        type: "POST",
                        datatype: "json",
                        contentType: "application/json; charset=utf-8",
                        data: JSON.stringify({ Record_ID: $self.record_ID }),
                        async: false,
                        success: function (data) {
                            gotRespStatus = JSON.parse(data);
                        },
                        error: function () {
                            SetBusy(false);
                        }
                    });
                }
                catch (error) {
                    alert(error);
                    SetBusy(false);
                }
            }
        };

        function SubmitToolResponse(run_ID, tool_ID) {
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/runs/' + run_ID + '/submit_tool_outputs',  // OpenAI's endpoint
                    type: 'POST',
                    contentType: 'application/json',
                    async: false,
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },
                    data: JSON.stringify({
                        "tool_outputs": [
                            {
                                "tool_call_id": tool_ID,
                                "output": gotRespStatus != "" ? gotRespStatus : "Success"
                            }
                        ]
                    }),

                    success: function (response) {
                        if (response != null) {
                            if (response.status == "completed") {
                                gotRespStatus = true;
                                //GetResponseMessage(response.id);
                            }
                        }
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });
            }
            catch
            {

            }
        };

        function GetPromptMessage() {
            SetBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_Lead/GetPromptMsg",
                type: "POST",
                data: { Table_ID: $self.table_ID, rec_ID: $self.record_ID },
                success: function (data) {
                    if (data != null) {
                        if (JSON.parse(data) != '') {
                            PromptMsg = JSON.parse(data);
                            CreateThread();
                        }
                        else {
                            PromptMsg = "RecPromptMsg Hi AI";
                            CreateThread();
                        }

                    }
                },
                error: function () {
                    //SetBusy(false);
                }
            });
        }

        function GetResponseMessage(run_id) {
            var r_id = run_id;
            runStatus = false;
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/messages', // OpenAI's endpoint
                    type: 'GET',
                    contentType: 'application/json',
                    //async: false,
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },

                    success: function (response) {
                        if (response != null) {
                            var data = response.data.filter((record) =>
                                record.run_id === r_id && record.role === "assistant");
                            if (data != null) {
                                appendMessageOutput(data[0].content[0].text.value);
                                if (newThread) {
                                    updateThreadID();
                                }
                                else {
                                    SetBusy(false);
                                }
                            }
                            else {
                                SetBusy(false);
                            }
                        }
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });

            }
            catch (error) {
                alert(error);
            }
        };

        function getThreadMessage() {
            try {
                $.ajax({
                    url: 'https://api.openai.com/v1/threads/' + thread_id + '/messages', // OpenAI's endpoint
                    type: 'GET',
                    contentType: 'application/json',
                    headers: {
                        'Authorization': "Bearer " + apiKey,
                        'OpenAI-Beta': 'assistants=v2'

                        // Add your OpenAI API key here
                    },
                    success: function (response) {
                        appendthreadmessage(response);
                    },
                    error: function (xhr, status, error) {
                        console.error("Error:", error);
                        SetBusy(false);
                    }
                });
            }
            catch (error) {
                alert(error);
            }
        };

        function appendthreadmessage(response) {
            $ResTxt.empty();
            for (var i = response.data.length - 1; i >= 0; i--) {
                if (response.data[i].role == "user") {
                    if (!response.data[i].content[0].text.value.contains("RecPromptMsg")) {
                        addUserMessage(response.data[i].content[0].text.value);
                    }
                }
                else if (response.data[i].role == "assistant")
                    appendMessageOutput(response.data[i].content[0].text.value);
            }
            SetBusy(false);
        };

        function scrollToBottom() {
            document.querySelector(".VAS-chat-body").scrollTop = document.querySelector(".VAS-chat-body").scrollHeight;
            $InputTxt.focus();
        }

        function appendMessageOutput(txt) {
            $ResTxt.append(' <div class= "VAS-userChat-comment" > ' +
                '<div class="VAS-thumbImg"><img src="Areas/VAS/Content/Images/AI.png" alt=""></div>' +
                '<div class="VAS-chat-comments-col VAS-chatBox-white">' +
                '  <div class="VAS-chatuser-name">AURA<span>12:00PM</span></div>' +
                ' <div class="VAS-chatBox">' + txt + '</div>' +
                '</div>' +
                '</div>');
            scrollToBottom();
        };

        function addUserMessage(txt) {
            $ResTxt.append(' <div class= "VAS-userChat-comment VAS-row-reverse" > ' +
                '  <div class="VAS-thumbImg"><img src="' + VIS.Application.contextFullUrl + imgUrl + '" alt=""></div>' +
                ' <div class="VAS-chat-comments-col ">' +
                '   <div class="VAS-chatuser-name VAS-row-reverse">' + VIS.context.getAD_User_Name() + '<span>12:00PM</span></div>' +
                '  <div class="VAS-chatBox VAS-chat-radius">' + txt + '</div>' +
                '</div>' +
                ' </div>');
            scrollToBottom();
        };

        this.getRoot = function () {
            return $root;
        };

        this.getThreadID = function (rec_ID) {
            SetBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_Lead/GetThreadID",
                type: "POST",
                data: { rec_ID: rec_ID },
                success: function (data) {
                    if (data != null) {
                        data = JSON.parse(data);
                        apiKey = data.APiKey;

                        if (data.ThreadID == '') {
                            SetBusy(true);
                            GetPromptMessage();
                        }
                        else {
                            thread_id = data.ThreadID;
                            $ResTxt.empty();
                            getThreadMessage();
                        };
                    }
                },
                error: function () {
                    //SetBusy(false);
                }
            });
        };


        this.getUserImg = function (rec_ID) {
            SetBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_Lead/GetUserImg",
                type: "POST",
                async: false,
                data: { rec_ID: rec_ID },
                success: function (data) {
                    if (data != null) {
                        imgUrl = JSON.parse(data);
                    }
                    else {

                    };
                }
                ,
                error: function () {
                    //SetBusy(false);
                }
            });
        };

        function updateThreadID() {
            var field = $self.record_ID + "," + thread_id;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_Lead/SetThreadID",
                type: "POST",
                data: {
                    'field': field
                },
                success: function (data) {
                    newThread = false;
                    SetBusy(false);
                },
                error: function () {
                    SetBusy(false);
                }
            });
        };
    };


    VAS.VAS_LeadConversation.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        this.table_ID = curTab.getAD_Table_ID();
        this.init();
    };

    /*This function to update tab panel based on selected record*/
    VAS.VAS_LeadConversation.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            //this.Clear();
        } else {
            this.record_ID = recordID;
            this.selectedRow = selectedRow;
            this.getUserImg(recordID);
            this.getThreadID(recordID);
        }
    };

    /*
     This will set width as per window width in dafault case it is 75%
     */
    VAS.VAS_LeadConversation.prototype.sizeChanged = function (width) {
        this.panelWidth = 25;
    };

    /*
    Release all variables from memory
    */
    VAS.VAS_LeadConversation.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.rowSource = null;
        this.panelWidth = null;
    }
})(VAS, jQuery);