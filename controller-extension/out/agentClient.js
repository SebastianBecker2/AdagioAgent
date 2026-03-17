"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.AgentClient = void 0;
exports.createAgentClient = createAgentClient;
const vscode = __importStar(require("vscode"));
/**
 * HTTP client that wraps all calls to the Windows VM agent REST API.
 */
class AgentClient {
    constructor(baseUrl) {
        this.baseUrl = baseUrl.replace(/\/$/, "");
    }
    // ─── Health ──────────────────────────────────────────────────────────────
    async health() {
        return this.get("/health");
    }
    // ─── Process ─────────────────────────────────────────────────────────────
    /**
     * Start an executable process on the VM.
     * @param request Run parameters (command, optional args, optional workingDir)
     */
    async runExecutable(request) {
        return this.post("/run", request);
    }
    // ─── UI Automation ───────────────────────────────────────────────────────
    /**
     * Retrieve the UI element tree for a running process.
     * @param pid Process ID returned by runExecutable
     */
    async getUiTree(pid) {
        return this.get(`/ui-tree?pid=${pid}`);
    }
    /**
     * Click a UI element by its element ID.
     * @param pid   Process ID
     * @param elementId  Element ID from the UI tree
     */
    async clickElement(pid, elementId) {
        const request = { pid, elementId };
        return this.post("/click", request);
    }
    /**
     * Type text into a UI element.
     * @param pid       Process ID
     * @param elementId Element ID from the UI tree
     * @param text      Text to type
     */
    async typeText(pid, elementId, text) {
        const request = { pid, elementId, text };
        return this.post("/type", request);
    }
    // ─── Screenshot ──────────────────────────────────────────────────────────
    /**
     * Capture a screenshot of the process window.
     * @param pid Process ID
     */
    async getScreenshot(pid) {
        return this.get(`/screenshot?pid=${pid}`);
    }
    // ─── Helpers ─────────────────────────────────────────────────────────────
    async get(path) {
        const url = `${this.baseUrl}${path}`;
        const response = await fetch(url);
        return this.handleResponse(response);
    }
    async post(path, body) {
        const url = `${this.baseUrl}${path}`;
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        });
        return this.handleResponse(response);
    }
    async handleResponse(response) {
        if (!response.ok) {
            let detail;
            try {
                const err = (await response.clone().json());
                detail = err.detail ?? err.error;
            }
            catch {
                detail = await response.text();
            }
            throw new Error(`VM agent responded with ${response.status}: ${detail ?? response.statusText}`);
        }
        return response.json();
    }
}
exports.AgentClient = AgentClient;
/**
 * Create an AgentClient using the URL from VS Code configuration.
 */
function createAgentClient() {
    const config = vscode.workspace.getConfiguration("adagioAgent");
    const url = config.get("vmAgentUrl") ?? "http://localhost:5000";
    return new AgentClient(url);
}
//# sourceMappingURL=agentClient.js.map