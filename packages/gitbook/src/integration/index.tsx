import {
  createComponent,
  createIntegration,
  type RuntimeContext,
  type RuntimeEnvironment,
} from '@gitbook/runtime'

import { AUTO_THEME, normalizeArtifactsUrl } from '../config.js'
import { renderFrameShell } from '../frame.js'

/** Space/site-level configuration, declared in `gitbook-manifest.yaml`. */
type GloSharpConfiguration = {
  /** Base URL the repo's CI publishes `<theme>/<sha256>.html` artifacts under. */
  artifactsUrl?: string
  /** `github-dark`, `github-light`, or `auto` to follow the reader's colour scheme. */
  theme?: string
}

type GloSharpContext = RuntimeContext<RuntimeEnvironment<{}, GloSharpConfiguration>>

const DEFAULT_SNIPPET = `var greeting = "Hello, Glo#";
//  ^?`

/** The block's ContentKit output only varies with its props, so cache it for a day. */
const BLOCK_CACHE_MAX_AGE = 86400

const snippetBlock = createComponent<
  { content?: string; theme?: string },
  { content: string },
  void,
  GloSharpContext
>({
  componentId: 'snippet',
  initialState: (props) => ({
    // `??`, not `||`: an author's deliberately empty fence is content, and
    // replacing it with the sample would change what the page says.
    content: props.content ?? DEFAULT_SNIPPET,
  }),
  async render(element, { environment }) {
    if (element.context.type !== 'document') {
      throw new Error('Invalid context')
    }

    const { editable } = element.context
    const { content } = element.state

    element.setCache({ maxAge: BLOCK_CACHE_MAX_AGE })

    const configuration =
      environment.spaceInstallation?.configuration ?? environment.siteInstallation?.configuration

    // publicContentEndpoint is the cookie-less origin GitBook provides for
    // exactly this: rendered integration content loaded in a webframe.
    const url = new URL(environment.integration.urls.publicContentEndpoint)
    url.searchParams.set('v', String(environment.integration.version))

    const frame = (
      <webframe
        source={{ url: url.toString() }}
        aspectRatio={16 / 9}
        data={{
          content: element.dynamicState('content'),
          artifacts: normalizeArtifactsUrl(configuration?.artifactsUrl),
          theme: element.props.theme || configuration?.theme || AUTO_THEME,
        }}
      />
    )

    // Editing shows the fence body in a real code block with the preview below;
    // readers only get the frame.
    return (
      <block>
        {editable ? (
          <codeblock
            state="content"
            content={content}
            syntax="csharp"
            onContentChange={{
              action: '@editor.node.updateProps',
              props: { content: element.dynamicState('content') },
            }}
            footer={[frame]}
          />
        ) : (
          frame
        )}
      </block>
    )
  },
})

// The shell is a pure function of its options, so build it once per isolate and
// serve the same bytes to every reader.
const FRAME_SHELL = renderFrameShell()

export default createIntegration<GloSharpContext>({
  fetch: async () =>
    new Response(FRAME_SHELL, {
      headers: {
        'Content-Type': 'text/html; charset=utf-8',
        'Cache-Control': 'public, max-age=86400',
      },
    }),
  components: [snippetBlock],
})
