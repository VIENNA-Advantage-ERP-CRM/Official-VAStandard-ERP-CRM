; VAS = window.VAS || {};

//self-invoking function
; (function (VAS, $) {
    VAS.AttachError = function (WindowNo,errorHtml) {
        this.frame;
        var $root;
        this.WindowNo;
        var $self = this;
        var $gridBody = null;

        // Initialize UI Elements
        function load() {
            $root = $("<div style='width: 100%; height: 100%; background-color: white; padding-right:10px'>");
            $gridBody = $("<div class='vas-errorBody'>").html(errorHtml);
            $root.append($gridBody);

        }
        
        this.show = function () {
            load();
            var ch = new VIS.ChildDialog(); //create object of child dialog
            ch.setHeight($(window).height() - 350);
            ch.setWidth($(window).width() - 700);
            ch.setTitle(VIS.Msg.getMsg("VAS_error"));
            ch.setContent($root); //set the content
            ch.show();
            ch.onClose = function () {

                if ($self.onClose) $self.onClose();
                $self.dispose();
            };

        }
        /*function used to deallocate the memory*/
        this.disposeComponent = function () {
            $root = null;
            WindowNo = null;
            $gridBody = null;
            this.disposeComponent = null;
        };

    };

    VAS.AttachError.prototype.dispose = function () {
        this.disposeComponent();
    };

    // Load form into VIS
    VAS.AttachError = VAS.AttachError;

})(VAS, jQuery);