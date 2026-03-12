import { useState, useRef, useEffect } from 'react'

interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
  timestamp: Date
}

export function ChatPanel({ apiBaseUrl }: { apiBaseUrl: string | undefined }) {
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      role: 'assistant',
      content: 'Hello! I\'m your Factory Operations Assistant. Ask me anything about machines, inventory, maintenance, work orders, or risk scores.',
      timestamp: new Date(),
    },
  ])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const messagesEndRef = useRef<HTMLDivElement>(null)

  const makeUrl = (path: string) =>
    apiBaseUrl ? new URL(path, apiBaseUrl).toString() : path

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const sendMessage = async () => {
    const question = input.trim()
    if (!question || loading) return

    setInput('')
    setMessages(prev => [...prev, { role: 'user', content: question, timestamp: new Date() }])
    setLoading(true)

    try {
      const response = await fetch(makeUrl('/api/chat'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question }),
      })
      const data = await response.json()

      if (data.error) {
        setMessages(prev => [...prev, { role: 'assistant', content: `Error: ${data.error}`, timestamp: new Date() }])
      } else {
        setMessages(prev => [...prev, { role: 'assistant', content: data.answer, timestamp: new Date() }])
      }
    } catch (e) {
      setMessages(prev => [...prev, {
        role: 'assistant',
        content: `Failed to get response: ${e instanceof Error ? e.message : 'Unknown error'}`,
        timestamp: new Date(),
      }])
    } finally {
      setLoading(false)
    }
  }

  const quickQuestions = [
    'Which machine has the highest risk score?',
    'Are any parts running low?',
    'How many open work orders are there?',
    'What is the total factory downtime?',
    'Which machines need maintenance soon?',
  ]

  return (
    <div className="chat-panel">
      <div className="chat-messages">
        {messages.map((msg, i) => (
          <div key={i} className={`chat-message chat-message--${msg.role}`}>
            <div className="chat-message__avatar">
              {msg.role === 'user' ? '👤' : '🤖'}
            </div>
            <div className="chat-message__content">
              <div className="chat-message__text">{msg.content}</div>
              <div className="chat-message__time">
                {msg.timestamp.toLocaleTimeString()}
              </div>
            </div>
          </div>
        ))}
        {loading && (
          <div className="chat-message chat-message--assistant">
            <div className="chat-message__avatar">🤖</div>
            <div className="chat-message__content">
              <div className="chat-typing">
                <span /><span /><span />
              </div>
            </div>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      <div className="chat-quick-questions">
        {quickQuestions.map((q, i) => (
          <button
            key={i}
            className="chat-quick-btn"
            onClick={() => { setInput(q); }}
            disabled={loading}
          >
            {q}
          </button>
        ))}
      </div>

      <form
        className="chat-input-form"
        onSubmit={(e) => { e.preventDefault(); sendMessage(); }}
      >
        <input
          type="text"
          className="chat-input"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Ask about factory operations..."
          disabled={loading}
        />
        <button type="submit" className="primary-button chat-send-btn" disabled={loading || !input.trim()}>
          {loading ? '...' : 'Send'}
        </button>
      </form>
    </div>
  )
}
