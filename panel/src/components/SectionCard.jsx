import { Card, Text, makeStyles, tokens } from '@fluentui/react-components'

const useStyles = makeStyles({
  card: {
    padding: '16px 18px',
    display: 'flex',
    flexDirection: 'column',
    gap: '14px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '10px',
    boxShadow: tokens.shadow2,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    flexWrap: 'wrap',
  },
  titleGroup: {
    display: 'flex',
    alignItems: 'baseline',
    gap: '10px',
    flexWrap: 'wrap',
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
  },
})

/** 统一的卡片外壳：标题、副标题、右侧操作区。全站卡片的间距与圆角由这里决定。 */
export function SectionCard({ title, subtitle, actions, children, className }) {
  const styles = useStyles()
  return (
    <Card className={[styles.card, className].filter(Boolean).join(' ')}>
      {(title || actions) && (
        <div className={styles.header}>
          <div className={styles.titleGroup}>
            {title ? <Text weight="semibold" size={400}>{title}</Text> : null}
            {subtitle ? <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{subtitle}</Text> : null}
          </div>
          {actions ? <div className={styles.actions}>{actions}</div> : null}
        </div>
      )}
      {children}
    </Card>
  )
}
