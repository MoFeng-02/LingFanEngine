// 灵泛引擎浏览器入口 —— Avalonia 加载 + DotNet 互操作
console.log("灵泛引擎 Browser 环境");

// ===== 音频互操作（通过 Web Audio API） =====
(function () {
    "use strict";

    // 初始化音频上下文
    let audioCtx;
    function getAudioCtx() {
        if (!audioCtx) {
            audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        }
        if (audioCtx.state === 'suspended') {
            audioCtx.resume();
        }
        return audioCtx;
    }

    // 当前活动音源
    let currentBgmSource = null;
    let currentBgmGain = null;
    let currentSeSource = null;
    let currentVoiceSource = null;

    // 播放 BGM
    window.PlayBgm = function (urlBase64, loop) {
        try {
            const url = atob(urlBase64);
            const ctx = getAudioCtx();
            const request = new XMLHttpRequest();
            request.open('GET', url, true);
            request.responseType = 'arraybuffer';
            request.onload = function () {
                ctx.decodeAudioData(request.response, function (buffer) {
                    if (currentBgmSource) {
                        currentBgmSource.stop();
                    }
                    currentBgmSource = ctx.createBufferSource();
                    currentBgmSource.buffer = buffer;
                    currentBgmSource.loop = !!loop;
                    currentBgmGain = ctx.createGain();
                    currentBgmGain.gain.value = 0.8;
                    currentBgmSource.connect(currentBgmGain);
                    currentBgmGain.connect(ctx.destination);
                    currentBgmSource.start(0);
                });
            };
            request.send();
        } catch (e) {
            console.warn("PlayBgm error:", e);
        }
    };

    // 停止 BGM
    window.StopBgm = function () {
        if (currentBgmSource) {
            try { currentBgmSource.stop(); } catch (e) { }
            currentBgmSource = null;
        }
    };

    // 暂停 BGM
    window.PauseBgm = function () {
        if (getAudioCtx().state === 'running') {
            getAudioCtx().suspend();
        }
    };

    // 恢复 BGM
    window.ResumeBgm = function () {
        getAudioCtx().resume();
    };

    // 播放音效
    window.PlaySe = function (urlBase64) {
        try {
            const url = atob(urlBase64);
            const ctx = getAudioCtx();
            const request = new XMLHttpRequest();
            request.open('GET', url, true);
            request.responseType = 'arraybuffer';
            request.onload = function () {
                ctx.decodeAudioData(request.response, function (buffer) {
                    if (currentSeSource) {
                        currentSeSource.stop();
                    }
                    currentSeSource = ctx.createBufferSource();
                    currentSeSource.buffer = buffer;
                    currentSeSource.connect(ctx.destination);
                    currentSeSource.start(0);
                });
            };
            request.send();
        } catch (e) {
            console.warn("PlaySe error:", e);
        }
    };

    // 设置 BGM 音量
    window.SetBgmVolume = function (volume) {
        if (currentBgmGain) {
            currentBgmGain.gain.value = volume;
        }
    };

    // 完全停止所有音频
    window.StopAllAudio = function () {
        window.StopBgm();
        if (currentSeSource) {
            try { currentSeSource.stop(); } catch (e) { }
            currentSeSource = null;
        }
        if (currentVoiceSource) {
            try { currentVoiceSource.stop(); } catch (e) { }
            currentVoiceSource = null;
        }
    };

    console.log("灵泛引擎 Web Audio API 就绪");
})();

// ===== 其他互操作辅助 =====
// 获取浏览器语言
window.GetBrowserLanguage = function () {
    return navigator.language || navigator.userLanguage || "en-US";
};

// 获取屏幕分辨率
window.GetScreenSize = function () {
    return JSON.stringify({
        width: window.innerWidth,
        height: window.innerHeight,
        dpr: window.devicePixelRatio || 1
    });
};

// 请求动画帧（用于 GameLoop 互操作）
window.RequestAnimationFrame = function (callback) {
    return requestAnimationFrame(callback);
};

// 控制台日志
window.EngineLog = function (msg) {
    console.log("[LingFanEngine]", msg);
};

// ===== 等待 Avalonia 加载 =====
console.log("等待 Avalonia 应用启动...");

// ==== Avalonia WebAssembly 入口 ====
// Avalonia.Web.Blazor 会自动注入 dotnet.js 和 runtime
// 这里仅做预热和互操作注册
