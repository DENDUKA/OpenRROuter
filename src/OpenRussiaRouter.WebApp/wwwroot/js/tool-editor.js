// Markdown image helper for /admin/tools/edit.
// Uploads a file to the page's UploadImage handler and inserts the returned Markdown at the
// caret of whichever Markdown textarea was last focused. Vanilla JS on purpose — the rest of
// this app ships no front-end framework.
(function () {
    "use strict";

    var dropzone = document.getElementById("toolImageDropzone");
    if (!dropzone) {
        return; // New tool: the panel is locked until the first save.
    }

    var toolId = dropzone.getAttribute("data-tool-id");
    var fileInput = document.getElementById("toolImageInput");
    var pickButton = document.getElementById("toolImagePick");
    var status = document.getElementById("toolImageStatus");
    var strip = document.getElementById("toolImageStrip");
    var editors = Array.prototype.slice.call(document.querySelectorAll("textarea.md-editor"));

    // Remember the last focused editor so a drop on the panel still knows where to insert.
    var lastEditor = editors.length > 0 ? editors[0] : null;
    editors.forEach(function (editor) {
        editor.addEventListener("focus", function () { lastEditor = editor; });
        editor.addEventListener("paste", function (event) {
            var file = imageFromClipboard(event.clipboardData);
            if (!file) {
                return;
            }
            event.preventDefault();
            lastEditor = editor;
            upload(file);
        });
    });

    pickButton.addEventListener("click", function () { fileInput.click(); });
    fileInput.addEventListener("change", function () {
        if (fileInput.files && fileInput.files.length > 0) {
            upload(fileInput.files[0]);
            fileInput.value = "";
        }
    });

    ["dragenter", "dragover"].forEach(function (name) {
        dropzone.addEventListener(name, function (event) {
            event.preventDefault();
            dropzone.classList.add("is-dragover");
        });
    });
    ["dragleave", "drop"].forEach(function (name) {
        dropzone.addEventListener(name, function (event) {
            event.preventDefault();
            dropzone.classList.remove("is-dragover");
        });
    });
    dropzone.addEventListener("drop", function (event) {
        var files = event.dataTransfer && event.dataTransfer.files;
        if (files && files.length > 0) {
            upload(files[0]);
        }
    });

    strip.addEventListener("click", function (event) {
        var button = event.target.closest(".md-copy");
        if (!button) {
            return;
        }
        insertAtCaret(button.getAttribute("data-markdown"));
        setStatus("Markdown inserted.", false);
    });

    function imageFromClipboard(clipboardData) {
        if (!clipboardData || !clipboardData.items) {
            return null;
        }
        for (var i = 0; i < clipboardData.items.length; i++) {
            var item = clipboardData.items[i];
            if (item.kind === "file" && item.type.indexOf("image/") === 0) {
                return item.getAsFile();
            }
        }
        return null;
    }

    function upload(file) {
        setStatus("Uploading " + file.name + "…", false);

        var body = new FormData();
        body.append("imageFile", file, file.name || "pasted-image.png");

        // Razor Pages named handlers require the antiforgery token.
        var tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
        var headers = {};
        if (tokenField) {
            headers["RequestVerificationToken"] = tokenField.value;
        }

        var url = "/admin/tools/edit/" + encodeURIComponent(toolId) +
            "?handler=UploadImage&id=" + encodeURIComponent(toolId);

        fetch(url, { method: "POST", body: body, headers: headers, credentials: "same-origin" })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error("HTTP " + response.status);
                }
                return response.json();
            })
            .then(function (result) {
                if (!result.ok) {
                    setStatus(result.error || "Upload failed.", true);
                    return;
                }
                insertAtCaret(result.markdown);
                addThumbnail(result);
                setStatus("Uploaded — Markdown inserted at the caret.", false);
            })
            .catch(function (error) {
                setStatus("Upload failed: " + error.message, true);
            });
    }

    function insertAtCaret(text) {
        if (!lastEditor) {
            return;
        }
        var start = lastEditor.selectionStart || 0;
        var end = lastEditor.selectionEnd || 0;
        var value = lastEditor.value;
        var snippet = "\n" + text + "\n";

        lastEditor.value = value.slice(0, start) + snippet + value.slice(end);
        var caret = start + snippet.length;
        lastEditor.setSelectionRange(caret, caret);
        lastEditor.focus();
    }

    function addThumbnail(result) {
        var markdown = result.markdown;
        var figure = document.createElement("figure");
        figure.className = "md-image-card";
        figure.setAttribute("data-url", result.url);

        var img = document.createElement("img");
        img.src = result.url;
        img.alt = result.fileName || "";
        img.loading = "lazy";

        var caption = document.createElement("figcaption");
        caption.textContent = result.fileName || result.id;
        caption.title = caption.textContent;

        var actions = document.createElement("div");
        actions.className = "md-image-actions";

        var copy = document.createElement("button");
        copy.type = "button";
        copy.className = "btn btn-sm btn-link md-copy";
        copy.setAttribute("data-markdown", markdown);
        copy.textContent = "Copy Markdown";
        actions.appendChild(copy);

        var hint = document.createElement("span");
        hint.className = "md-image-hint";
        hint.textContent = "reload to enable delete";
        actions.appendChild(hint);

        figure.appendChild(img);
        figure.appendChild(caption);
        figure.appendChild(actions);
        strip.insertBefore(figure, strip.firstChild);
    }

    function setStatus(message, isError) {
        status.textContent = message;
        status.classList.toggle("is-error", !!isError);
    }
})();
