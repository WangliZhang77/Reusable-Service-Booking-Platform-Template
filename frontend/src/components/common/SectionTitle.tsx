type SectionTitleProps = {
  eyebrow?: string
  title: string
  description?: string
}

function SectionTitle({ eyebrow, title, description }: SectionTitleProps) {
  return (
    <div className="section-title">
      {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
      <h2>{title}</h2>
      {description ? <p className="muted">{description}</p> : null}
    </div>
  )
}

export default SectionTitle
