// Project-local `/go` command.
//
// OpenCode custom commands are prompt templates and cannot drive the standing
// goal loop. This small plugin registers a real command that reads the goal
// contract and delegates to the opencode-goal plugin's `/goal` command.
import { readFile } from "node:fs/promises"
import { join } from "node:path"

const GOAL_FILE = ".opencode/goals/desktop-v1.md"
const BUDGET = "120"

export default {
  id: "rok-mahjong.go",
  async setup(ctx) {
    await ctx.command.transform((editor) => {
      editor.add({
        name: "go",
        description: "Start the standing goal to deliver the playable desktop v1",
        execute: async ({ sessionID, delivery }) => {
          const roots = [ctx.location.directory, ctx.location.project?.canonical].filter(Boolean)
          let goal = ""
          let errorMessage = ""

          for (const root of roots) {
            try {
              goal = (await readFile(join(root, GOAL_FILE), "utf8")).trim()
              break
            } catch (error) {
              errorMessage = error instanceof Error ? error.message : String(error)
            }
          }

          if (!goal) {
            await ctx.session.synthetic({
              sessionID,
              text: `Cannot read ${GOAL_FILE}. ${errorMessage}`,
            })
            return
          }

          try {
            // Give the long-running desktop goal a practical turn budget. The
            // user can change it with /goal budget at any time.
            await ctx.session.command({
              sessionID,
              name: "goal",
              text: `budget ${BUDGET}`,
              delivery,
            })
            await ctx.session.command({
              sessionID,
              name: "goal",
              text: goal,
              delivery,
            })
          } catch (error) {
            await ctx.session.synthetic({
              sessionID,
              text:
                "The /go command delegates to /goal, but that command failed. " +
                "Make sure the opencode-goal plugin is installed and loaded. " +
                (error instanceof Error ? error.message : String(error)),
            })
          }
        },
      })
    })
  },
}
