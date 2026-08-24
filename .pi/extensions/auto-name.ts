/**
 * Auto-name pi sessions from submitted prompts.
 *
 * Fires a cheap OpenRouter model to summarise the first message into <=6 words
 * and calls both `pi.setSessionName()` and `ctx.ui.setTitle()`.  The session
 * name persists the summary in pi's session state so it survives restarts;
 * `setTitle` then overrides the window/tab title to just the summary (without
 * the `π -` prefix or project postfix pi would normally add).
 * Fire-and-forget: the agent starts immediately, the title arrives a beat later.
 *
 * Needs `OPENROUTER_API_KEY` in the environment (the pi sandbox already
 * forwards it). `SLOPWORLD_PI_TITLES` selects never, once or always and
 * `SLOPWORLD_PI_TITLE_MODEL` overrides the model. Both arrive from slopd's
 * Usage settings when the session starts. Silently skips if the key is
 * missing, the policy says not to name, or the summariser fails.
 *
 * Project-local extension (`.pi/extensions/`), loaded when pi's cwd is inside
 * the slopworld project directory and never installed globally or bind-mounted.
 */

import type { ExtensionAPI, ExtensionContext } from "@earendil-works/pi-coding-agent";

const DEFAULT_MODEL = "google/gemini-3.1-flash-lite";
const TIMEOUT_MS = 5_000;
const MAX_PROMPT_CHARS = 2000;
const MAX_TITLE_CHARS = 60;

export default function (pi: ExtensionAPI) {
	pi.on("before_agent_start", async (event, ctx) => {
		const policy = process.env.SLOPWORLD_PI_TITLES ?? "always";
		if (policy === "never") return;
		// A manual `/name` or a previous summary owns the title in once mode.
		if (policy === "once" && pi.getSessionName() !== undefined) return;

		const prompt = (event.prompt ?? "").trim();
		if (!prompt) return;

		const key = process.env.OPENROUTER_API_KEY;
		if (!key) return;

		// Truncate to keep the summariser cheap and fast.
		// A full diff or log is wasted on a 6-word title.
		const text = prompt.slice(0, MAX_PROMPT_CHARS);

		// Fire-and-forget: the agent starts immediately, the title arrives
		// a beat later.  Own timeout so we don't depend on the turn's signal.
		const model = process.env.SLOPWORLD_PI_TITLE_MODEL || DEFAULT_MODEL;
		nameFromPrompt(text, key, model, pi, ctx).catch(() => {});
	});
}

async function nameFromPrompt(
	text: string,
	key: string,
	model: string,
	pi: ExtensionAPI,
	ctx: ExtensionContext,
): Promise<void> {
	const ac = new AbortController();
	const timer = setTimeout(() => ac.abort(), TIMEOUT_MS);

	try {
		const res = await fetch(
			"https://openrouter.ai/api/v1/chat/completions",
			{
				method: "POST",
				headers: {
					Authorization: `Bearer ${key}`,
					"Content-Type": "application/json",
				},
				body: JSON.stringify({
					model,
					messages: [
						{
							role: "user",
							content: [
								"Summarise this coding request in ≤6 words for a session title.",
								"Reply with only the title — no quotes, no punctuation, no commentary.",
								"",
								text,
							].join("\n"),
						},
					],
					max_tokens: 24,
					temperature: 0,
				}),
				signal: ac.signal,
			},
		);

		if (!res.ok) {
			// 401 / 402 / 429 are common and mean nothing actionable here.
			return;
		}

		const body = (await res.json()) as {
			choices?: Array<{ message?: { content?: string } }>;
		};
		const raw = body?.choices?.[0]?.message?.content?.trim() ?? "";
		if (!raw) return;

		// Strip any remaining quotes, trailing punctuation, and truncate.
		const title = raw
			.replace(/^["'\u201C\u201D]+|["'\u201C\u201D]+$/g, "")
			.replace(/[.!]+$/, "")
			.slice(0, MAX_TITLE_CHARS)
			.trim();

		if (title) {
			// Persist the summary in pi's session state so it survives
			// across daemon/game restarts.  pi.setSessionName() fires
			// session_info_changed which triggers updateTerminalTitle()
			// setting the title to "π - summary - project"; the
			// ctx.ui.setTitle() call below overrides it to just the
			// clean summary, so the window/tab title shows only what
			// the summariser produced.
			pi.setSessionName(title);
			ctx.ui.setTitle(title);
		}
	} catch {
		// Network error, timeout, aborted — nothing to do.
		// The session stays unnamed and `/name` still works.
	} finally {
		clearTimeout(timer);
	}
}
